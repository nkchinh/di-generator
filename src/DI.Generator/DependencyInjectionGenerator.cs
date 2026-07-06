using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NkChinh.DI.Generator;

/// <summary>
/// Incremental source generator providing attribute-driven service registration and
/// [Inject] constructor generation for Microsoft.Extensions.DependencyInjection.
/// </summary>
/// <remarks>
/// Pipelines:
/// <list type="bullet">
/// <item>Post-init: embeds the DIGen attributes as internal types into the consuming project.</item>
/// <item>Services: classes with lifetime attributes → per-assembly Add{Assembly}Services extension
/// plus an assembly-level module marker; referenced markers → Add{Assembly}AllServices aggregator.</item>
/// <item>Injects: [Inject] fields/properties grouped per class → one generated constructor.</item>
/// </list>
/// </remarks>
[Generator]
public sealed class DependencyInjectionGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
            ctx.AddSource(
                EmbeddedSources.AttributesHintName,
                SourceText.From(EmbeddedSources.Attributes, Encoding.UTF8)));

        var services = CollectServices(context).WithTrackingName("Services");

        var assemblyName = context.CompilationProvider
            .Select(static (compilation, _) => compilation.AssemblyName ?? "Assembly")
            .WithTrackingName("AssemblyName");

        var referencedModules = context.CompilationProvider
            .Select(static (compilation, _) => Parsers.GetReferencedModules(compilation))
            .WithTrackingName("Modules");

        context.RegisterSourceOutput(
            services.Combine(assemblyName).Combine(referencedModules),
            static (spc, input) => Emitters.EmitRegistrations(spc, input.Left.Left, input.Left.Right, input.Right));

        var injects = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "DIGen.InjectAttribute",
                static (node, _) => node is VariableDeclaratorSyntax or PropertyDeclarationSyntax,
                static (ctx, _) => Parsers.GetInjectResult(ctx))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!)
            .WithTrackingName("Injects");

        context.RegisterSourceOutput(
            injects.Collect(),
            static (spc, results) => Emitters.EmitConstructors(spc, results));
    }

    private static IncrementalValueProvider<EquatableArray<ServiceResult>> CollectServices(
        IncrementalGeneratorInitializationContext context)
    {
        var singletonSelf = ForLifetime(context, "DIGen.SingletonServiceAttribute", "Singleton");
        var singletonTyped = ForLifetime(context, "DIGen.SingletonServiceAttribute`1", "Singleton");
        var scopedSelf = ForLifetime(context, "DIGen.ScopedServiceAttribute", "Scoped");
        var scopedTyped = ForLifetime(context, "DIGen.ScopedServiceAttribute`1", "Scoped");
        var transientSelf = ForLifetime(context, "DIGen.TransientServiceAttribute", "Transient");
        var transientTyped = ForLifetime(context, "DIGen.TransientServiceAttribute`1", "Transient");

        return singletonSelf.Collect()
            .Combine(singletonTyped.Collect())
            .Combine(scopedSelf.Collect())
            .Combine(scopedTyped.Collect())
            .Combine(transientSelf.Collect())
            .Combine(transientTyped.Collect())
            .Select(static (input, _) =>
            {
                var (((((singletonSelf, singletonTyped), scopedSelf), scopedTyped), transientSelf), transientTyped) = input;
                var results = new List<ServiceResult>(
                    singletonSelf.Length + singletonTyped.Length +
                    scopedSelf.Length + scopedTyped.Length +
                    transientSelf.Length + transientTyped.Length);
                results.AddRange(singletonSelf);
                results.AddRange(singletonTyped);
                results.AddRange(scopedSelf);
                results.AddRange(scopedTyped);
                results.AddRange(transientSelf);
                results.AddRange(transientTyped);
                return new EquatableArray<ServiceResult>(results.ToArray());
            });
    }

    private static IncrementalValuesProvider<ServiceResult> ForLifetime(
        IncrementalGeneratorInitializationContext context,
        string attributeMetadataName,
        string lifetime)
        => context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                (ctx, _) => Parsers.GetServiceResult(ctx, lifetime))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);
}
