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

        var publishedDefinitions = context.CompilationProvider
            .Select(static (compilation, _) => Parsers.ReadReferencedServiceDefinitions(compilation))
            .WithTrackingName("ReferencesScan");

        var externalScopeRules = context.CompilationProvider
            .Select(static (compilation, _) => Parsers.GetExternalScopeRules(compilation))
            .WithTrackingName("ExternalScopeRules");

        var hasServiceLifetime = context.CompilationProvider
            .Select(static (compilation, _) =>
                compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.ServiceLifetime") is not null)
            .WithTrackingName("HasServiceLifetime");

        context.RegisterSourceOutput(
            hasServiceLifetime, static (spc, hasServiceLifetime) =>
            {
                if (hasServiceLifetime)
                {
                    spc.AddSource(
                        EmbeddedSources.LifetimeExtensionsHintName,
                        SourceText.From(EmbeddedSources.LifetimeExtensions, Encoding.UTF8));
                }
            });

        var injects = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "DIGen.InjectAttribute",
                static (node, _) => node is VariableDeclaratorSyntax or PropertyDeclarationSyntax,
                static (ctx, _) => Parsers.GetInjectResult(ctx))
            .Where(static result => result is not null)
            .Select(static (result, _) => result ??
                throw new InvalidOperationException("Inject parser returned null after filtering."))
            .WithTrackingName("Injects");

        var collectedInjects = injects.Collect().WithTrackingName("CollectedInjects");

        var injectMeta = collectedInjects
            .Select(static (results, _) => Emitters.BuildInjectMeta(results))
            .WithTrackingName("InjectMeta");

        // Emit generated [Inject] constructors (partial-class files).
        context.RegisterSourceOutput(
            collectedInjects,
            static (spc, results) => Emitters.EmitConstructors(spc, results));

        var registrationInput = services
            .Combine(assemblyName)
            .Combine(externalScopeRules)
            .Combine(hasServiceLifetime)
            .Combine(injectMeta)
            .Combine(publishedDefinitions)
            .Select(static (input, _) => new RegistrationPipelineInput(
                input.Left.Left.Left.Left.Left,
                input.Left.Left.Left.Left.Right,
                input.Left.Left.Left.Right,
                input.Left.Left.Right,
                input.Left.Right,
                input.Right))
            .WithTrackingName("RegistrationInput");

        context.RegisterSourceOutput(
            registrationInput,
            static (spc, input) => Emitters.EmitRegistrations(
                spc,
                input.Services,
                input.AssemblyName,
                input.ExternalScopeRules,
                input.HasServiceLifetime,
                input.InjectMeta,
                input.PublishedDefinitions));
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
        var autoScoped = ForLifetime(context, "DIGen.ServiceAttribute`1", lifetime: null);

        return singletonSelf.Collect()
            .Combine(singletonTyped.Collect())
            .Combine(scopedSelf.Collect())
            .Combine(scopedTyped.Collect())
            .Combine(transientSelf.Collect())
            .Combine(transientTyped.Collect())
            .Combine(autoScoped.Collect())
            .Select(static (input, _) =>
            {
                var ((((((singletonSelf, singletonTyped), scopedSelf), scopedTyped), transientSelf), transientTyped), autoScoped) = input;
                var results = new List<ServiceResult>(
                    singletonSelf.Length + singletonTyped.Length +
                    scopedSelf.Length + scopedTyped.Length +
                    transientSelf.Length + transientTyped.Length + autoScoped.Length);
                results.AddRange(singletonSelf);
                results.AddRange(singletonTyped);
                results.AddRange(scopedSelf);
                results.AddRange(scopedTyped);
                results.AddRange(transientSelf);
                results.AddRange(transientTyped);
                results.AddRange(autoScoped);
                return new EquatableArray<ServiceResult>(results.ToArray());
            });
    }

    private static IncrementalValuesProvider<ServiceResult> ForLifetime(
        IncrementalGeneratorInitializationContext context,
        string attributeMetadataName,
        string? lifetime)
        => context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                (ctx, _) => Parsers.GetServiceResult(ctx, lifetime))
            .Where(static result => result is not null)
            .Select(static (result, _) => result ??
                throw new InvalidOperationException("Service parser returned null after filtering."));
}
