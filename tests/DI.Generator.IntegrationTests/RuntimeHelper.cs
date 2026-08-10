using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace NkChinh.DI.Generator.IntegrationTests;

/// <summary>
/// Runs the generator on real source code, emits a real assembly, and loads it so the
/// generated registration/constructor code can be executed against a live ServiceCollection.
/// </summary>
internal static class RuntimeHelper
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    private static readonly Lazy<ImmutableArray<MetadataReference>> LazyReferences = new(static () =>
    {
        var paths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES is unavailable."))
            .Split(Path.PathSeparator)
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Append(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location)
            .Append(typeof(Microsoft.Extensions.Hosting.IHostedService).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return paths
            .Select(static p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToImmutableArray();
    });

    public static byte[] CompileWithGenerator(
        string source,
        string assemblyName,
        params byte[][] referencedImages)
    {
        var references = LazyReferences.Value
            .AddRange(referencedImages.Select(static image => MetadataReference.CreateFromImage(image)));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, ParseOptions, path: $"{assemblyName}.cs")],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new DependencyInjectionGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var errors = diagnostics
            .Concat(outputCompilation.GetDiagnostics())
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Compilation of '{assemblyName}' failed:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
        }

        using var stream = new MemoryStream();
        var emitResult = outputCompilation.Emit(stream);
        if (!emitResult.Success)
        {
            throw new InvalidOperationException(
                $"Emit of '{assemblyName}' failed:{Environment.NewLine}" +
                string.Join(Environment.NewLine, emitResult.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));
        }

        return stream.ToArray();
    }

    public static Assembly Load(byte[] image, params (string Name, byte[] Image)[] dependencies)
    {
        var context = new InMemoryLoadContext(dependencies.ToDictionary(
            static d => d.Name,
            static d => d.Image,
            StringComparer.OrdinalIgnoreCase));
        using var stream = new MemoryStream(image);
        return context.LoadFromStream(stream);
    }

    /// <summary>
    /// Loads emitted assemblies from memory; everything else (Microsoft.Extensions.*) falls back
    /// to the default load context so type identity is shared with the test.
    /// </summary>
    private sealed class InMemoryLoadContext(Dictionary<string, byte[]> images)
        : AssemblyLoadContext(isCollectible: false)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is not null && images.TryGetValue(assemblyName.Name, out var image))
            {
                using var stream = new MemoryStream(image);
                return LoadFromStream(stream);
            }

            return null; // fall back to the default context
        }
    }
}
