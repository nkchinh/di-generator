using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace NkChinh.DI.Generator.Tests;

internal static class GeneratorTestHelper
{
    public static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    private static readonly Lazy<ImmutableArray<MetadataReference>> LazyReferences = new(static () =>
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Append(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location)
            .Append(typeof(Microsoft.Extensions.Hosting.IHostedService).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return paths
            .Select(static p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToImmutableArray();
    });

    public static ImmutableArray<MetadataReference> References => LazyReferences.Value;

    public static CSharpCompilation CreateCompilation(
        IEnumerable<string> sources,
        string assemblyName = "TestAssembly",
        IEnumerable<MetadataReference>? extraReferences = null)
    {
        var trees = sources.Select((s, i) =>
            CSharpSyntaxTree.ParseText(s, ParseOptions, path: $"TestSource{i}.cs"));

        return CSharpCompilation.Create(
            assemblyName,
            trees,
            extraReferences is null ? References : References.AddRange(extraReferences),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    public static GeneratorRunOutcome Run(
        string source,
        string assemblyName = "TestAssembly",
        IEnumerable<MetadataReference>? extraReferences = null)
        => Run([source], assemblyName, extraReferences);

    public static GeneratorRunOutcome Run(
        IEnumerable<string> sources,
        string assemblyName = "TestAssembly",
        IEnumerable<MetadataReference>? extraReferences = null)
    {
        var compilation = CreateCompilation(sources, assemblyName, extraReferences);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new DependencyInjectionGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var outputCompilation, out var generatorDiagnostics);

        return new GeneratorRunOutcome(driver.GetRunResult(), outputCompilation, generatorDiagnostics);
    }

    /// <summary>Emits a compilation to an in-memory assembly image, failing loudly on errors.</summary>
    public static byte[] EmitAssembly(Compilation compilation)
    {
        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);
        if (!result.Success)
        {
            var errors = string.Join(
                Environment.NewLine,
                result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException($"Emit failed:{Environment.NewLine}{errors}");
        }

        return ms.ToArray();
    }
}

internal sealed record GeneratorRunOutcome(
    GeneratorDriverRunResult Result,
    Compilation OutputCompilation,
    ImmutableArray<Diagnostic> GeneratorDiagnostics)
{
    /// <summary>Errors produced when compiling user code together with all generated code.</summary>
    public IReadOnlyList<Diagnostic> CompilationErrors =>
        OutputCompilation.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

    public bool HasSource(string hintNameSuffix) => TryGetTree(hintNameSuffix) is not null;

    public string GetSource(string hintNameSuffix) =>
        TryGetTree(hintNameSuffix)?.GetText().ToString()
        ?? throw new InvalidOperationException(
            $"No generated tree matching '{hintNameSuffix}'. Trees: " +
            string.Join(", ", Result.GeneratedTrees.Select(static t => t.FilePath)));

    private SyntaxTree? TryGetTree(string hintNameSuffix) =>
        Result.GeneratedTrees.FirstOrDefault(t =>
            t.FilePath.Replace('\\', '/').EndsWith(hintNameSuffix, StringComparison.Ordinal));
}
