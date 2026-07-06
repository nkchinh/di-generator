using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace NkChinh.DI.Generator;

internal static class Emitters
{
    private const string ServiceCollectionFqn = "global::Microsoft.Extensions.DependencyInjection.IServiceCollection";
    public const string RegistrationsHintName = "ServiceCollectionExtensions.g.cs";

    // ---------------------------------------------------------------- registrations

    // Framework-types-only tuple: identical across every assembly, unlike a project-embedded class or enum,
    // so passing it as a Collect{Assembly}Services parameter works safely across project references.
    private const string DescriptorTupleFqn =
        "(global::System.Type ServiceType, global::System.Type ImplementationType, int Lifetime, string? Key, bool IsHostedService)";
    private const string DescriptorListFqn = "global::System.Collections.Generic.ICollection<" + DescriptorTupleFqn + ">";

    public static void EmitRegistrations(
        SourceProductionContext context,
        EquatableArray<ServiceResult> results,
        string assemblyName,
        EquatableArray<ModuleInfo> modules,
        ExternalScopeRules externalScopeRules,
        bool hasMedi)
    {
        ReportDiagnostics(context, results.Select(static r => r.Diagnostic));
        ReportDiagnostics(context, externalScopeRules.Diagnostics.Select(static d => (DiagnosticInfo?)d));

        var externalScopeByType = externalScopeRules.Rules.ToDictionary(
            static r => r.TypeFqn, static r => r.Lifetime, StringComparer.Ordinal);

        var services = ResolveValidServices(context, results, externalScopeByType);
        var hasOwnServices = services.Count > 0;
        var hasModules = modules.Count > 0;
        if (!hasOwnServices && !(hasModules && hasMedi))
        {
            return;
        }

        var identifier = NameHelper.SanitizeAssemblyIdentifier(assemblyName);
        var className = identifier + "ServiceCollectionExtensions";
        var collectMethodName = "Collect" + identifier + "Services";
        var addMethodName = "Add" + identifier + "Services";
        var aggregatorName = "Add" + identifier + "AllServices";

        var builder = new StringBuilder();
        AppendFileHeader(builder);

        if (hasOwnServices)
        {
            builder.AppendLine(
                "[assembly: global::DIGen.Generated.ServiceRegistrationModuleAttribute(" +
                $"\"{collectMethodName}\", \"Microsoft.Extensions.DependencyInjection.{className}\")]");
            builder.AppendLine();
        }

        builder.AppendLine("namespace Microsoft.Extensions.DependencyInjection");
        builder.AppendLine("{");
        builder.AppendLine("    /// <summary>");
        builder.AppendLine($"    /// Dependency-injection registrations generated for assembly '{assemblyName}'.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine($"    [global::System.CodeDom.Compiler.GeneratedCode(\"{GeneratorInfo.Name}\", \"{GeneratorInfo.Version}\")]");
        builder.AppendLine($"    public static class {className}");
        builder.AppendLine("    {");

        if (hasOwnServices)
        {
            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// Collects all services declared with DIGen attributes in assembly '{assemblyName}' as plain");
            builder.AppendLine("        /// data, requiring no reference to Microsoft.Extensions.DependencyInjection.");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        public static void {collectMethodName}({DescriptorListFqn} registrations)");
            builder.AppendLine("        {");
            foreach (var service in services)
            {
                builder.AppendLine("            " + FormatDescriptor(service));
            }

            builder.AppendLine("        }");

            if (hasMedi)
            {
                builder.AppendLine();
                builder.AppendLine("        /// <summary>");
                builder.AppendLine($"        /// Registers all services declared with DIGen attributes in assembly '{assemblyName}'.");
                builder.AppendLine("        /// </summary>");
                builder.AppendLine($"        public static {ServiceCollectionFqn} {addMethodName}(this {ServiceCollectionFqn} services)");
                builder.AppendLine("        {");
                builder.AppendLine($"            var registrations = new global::System.Collections.Generic.List<{DescriptorTupleFqn}>();");
                builder.AppendLine($"            {collectMethodName}(registrations);");
                builder.AppendLine("            return global::DIGen.ServiceRegistrationExtensions.MaterializeServices(services, registrations);");
                builder.AppendLine("        }");
            }
        }

        if (hasMedi && hasModules)
        {
            if (hasOwnServices)
            {
                builder.AppendLine();
            }

            builder.AppendLine("        /// <summary>");
            builder.AppendLine("        /// Registers services from every referenced project generated by NkChinh.DI.Generator,");
            builder.AppendLine($"        /// followed by the services of assembly '{assemblyName}' itself.");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        public static {ServiceCollectionFqn} {aggregatorName}(this {ServiceCollectionFqn} services)");
            builder.AppendLine("        {");
            builder.AppendLine($"            var registrations = new global::System.Collections.Generic.List<{DescriptorTupleFqn}>();");
            foreach (var module in modules)
            {
                builder.AppendLine($"            global::{module.ExtensionsTypeName}.{module.MethodName}(registrations);");
            }

            if (hasOwnServices)
            {
                builder.AppendLine($"            {collectMethodName}(registrations);");
            }

            builder.AppendLine("            return global::DIGen.ServiceRegistrationExtensions.MaterializeServices(services, registrations);");
            builder.AppendLine("        }");
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");

        context.AddSource(RegistrationsHintName, SourceText.From(builder.ToString(), Encoding.UTF8));
    }

    private static List<ServiceInfo> ResolveValidServices(
        SourceProductionContext context,
        EquatableArray<ServiceResult> results,
        IReadOnlyDictionary<string, string> externalScopeByType)
    {
        var valid = new List<ServiceInfo>();
        var scopeResolved = ResolveLockedScopes(
            context,
            results.Where(static r => r.Service is not null).Select(static r => r.Service!),
            externalScopeByType);
        var groups = scopeResolved.GroupBy(static s => s.ImplementationFqn, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var entries = group.ToList();
            if (entries.Count > 1)
            {
                // The class carries more than one lifetime attribute.
                var display = TrimGlobalPrefix(group.Key);
                context.ReportDiagnostic(
                    DiagnosticInfo.Create(
                        DiagnosticDescriptors.MultipleLifetimeAttributes,
                        entries[0].Location,
                        display).ToDiagnostic());
                continue;
            }

            valid.Add(entries[0]);
        }

        valid.Sort(static (a, b) =>
        {
            var byImpl = string.CompareOrdinal(a.ImplementationFqn, b.ImplementationFqn);
            if (byImpl != 0)
            {
                return byImpl;
            }

            var byService = string.CompareOrdinal(a.ServiceFqn, b.ServiceFqn);
            return byService != 0 ? byService : string.CompareOrdinal(a.Key, b.Key);
        });
        return valid;
    }

    /// <summary>
    /// Applies the RequiredScope/RequiredExternalScope lock (if any) to every service that names a
    /// ServiceFqn: finalizes [Service&lt;T&gt;]'s lifetime (DIGEN008 if unresolvable), and checks explicit
    /// lifetime attributes against the lock (DIGEN009). Services that fail are dropped, not emitted.
    /// </summary>
    private static List<ServiceInfo> ResolveLockedScopes(
        SourceProductionContext context,
        IEnumerable<ServiceInfo> services,
        IReadOnlyDictionary<string, string> externalScopeByType)
    {
        var resolved = new List<ServiceInfo>();
        foreach (var service in services)
        {
            if (service.ServiceFqn is null)
            {
                // Self-registration has no TService to look up a lock by; out of scope for v1.
                resolved.Add(service);
                continue;
            }

            var locked = service.LockedLifetime ??
                (externalScopeByType.TryGetValue(service.ServiceFqn, out var external) ? external : null);

            if (service.IsAutoScope)
            {
                if (locked is null)
                {
                    context.ReportDiagnostic(
                        DiagnosticInfo.Create(
                            DiagnosticDescriptors.ServiceAttributeRequiresLockedScope,
                            service.Location,
                            TrimGlobalPrefix(service.ImplementationFqn),
                            TrimGlobalPrefix(service.ServiceFqn)).ToDiagnostic());
                    continue;
                }

                resolved.Add(service with { Lifetime = locked });
                continue;
            }

            if (locked is not null && locked != service.Lifetime)
            {
                context.ReportDiagnostic(
                    DiagnosticInfo.Create(
                        DiagnosticDescriptors.LifetimeDisagreesWithLockedScope,
                        service.Location,
                        TrimGlobalPrefix(service.ImplementationFqn),
                        TrimGlobalPrefix(service.ServiceFqn),
                        service.Lifetime!,
                        locked).ToDiagnostic());
                continue;
            }

            resolved.Add(service);
        }

        return resolved;
    }

    private static string FormatDescriptor(ServiceInfo service)
    {
        if (service.IsHostedService)
        {
            // Hosted services always register as IHostedService, ignoring any TService/key —
            // matches services.AddHostedService<Impl>() semantics.
            return "registrations.Add((" +
                $"typeof(global::Microsoft.Extensions.Hosting.IHostedService), typeof({service.ImplementationFqn}), " +
                $"(int)global::DIGen.DiServiceScope.{service.Lifetime}, null, true));";
        }

        var target = service.ServiceFqn ?? service.ImplementationFqn;
        var keyArg = service.Key is null ? "null" : SymbolDisplay.FormatLiteral(service.Key, quote: true);
        return "registrations.Add((" +
            $"typeof({target}), typeof({service.ImplementationFqn}), " +
            $"(int)global::DIGen.DiServiceScope.{service.Lifetime}, {keyArg}, false));";
    }

    // ---------------------------------------------------------------- [Inject] constructors

    public static void EmitConstructors(
        SourceProductionContext context,
        IReadOnlyList<InjectResult> results)
    {
        ReportDiagnostics(context, results.Select(static r => r.Diagnostic));

        // Skip constructor generation for any class that produced an error diagnostic.
        var failedGroups = new HashSet<string>(
            results
                .Where(static r => r.Diagnostic is { Descriptor.DefaultSeverity: DiagnosticSeverity.Error } &&
                                   r.GroupKey is not null)
                .Select(static r => r.GroupKey!),
            StringComparer.Ordinal);

        var groups = results
            .Where(static r => r.Shell is not null && r.Member is not null)
            .GroupBy(static r => r.Shell!.GroupKey, StringComparer.Ordinal)
            .OrderBy(static g => g.Key, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            if (failedGroups.Contains(group.Key))
            {
                continue;
            }

            var shell = group.First().Shell!;
            var members = group
                .Select(static r => r.Member!)
                .Distinct()
                .OrderBy(static m => m.FilePath, StringComparer.Ordinal)
                .ThenBy(static m => m.SpanStart)
                .ToList();

            context.AddSource(shell.HintName, SourceText.From(EmitConstructor(shell, members), Encoding.UTF8));
        }
    }

    private static string EmitConstructor(InjectClassShell shell, IReadOnlyList<InjectMemberInfo> members)
    {
        var parameterNames = NameHelper.AssignParameterNames(
            members.Select(static m => (m.TypeShortName, m.MemberName)).ToList());

        var builder = new StringBuilder();
        AppendFileHeader(builder);

        var indent = 0;
        if (shell.Namespace is not null)
        {
            builder.AppendLine($"namespace {shell.Namespace}");
            builder.AppendLine("{");
            indent++;
        }

        foreach (var type in shell.TypeChain)
        {
            builder.AppendLine($"{Pad(indent)}partial {type.Keyword} {type.DisplayName}");
            builder.AppendLine($"{Pad(indent)}{{");
            indent++;
        }

        var parameters = string.Join(
            ", ",
            members.Select((m, i) => $"{m.TypeFqn} {parameterNames[i]}"));

        builder.AppendLine($"{Pad(indent)}/// <summary>");
        builder.AppendLine($"{Pad(indent)}/// Constructor generated by {GeneratorInfo.Name} from [Inject] members.");
        builder.AppendLine($"{Pad(indent)}/// </summary>");
        builder.AppendLine($"{Pad(indent)}[global::Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]");
        builder.AppendLine($"{Pad(indent)}public {shell.ConstructorName}({parameters})");
        builder.AppendLine($"{Pad(indent)}{{");
        for (var i = 0; i < members.Count; i++)
        {
            builder.AppendLine($"{Pad(indent + 1)}this.{members[i].MemberName} = {parameterNames[i]};");
        }

        builder.AppendLine($"{Pad(indent)}}}");

        while (indent > 0)
        {
            indent--;
            builder.AppendLine($"{Pad(indent)}}}");
        }

        return builder.ToString();
    }

    // ---------------------------------------------------------------- helpers

    private static void AppendFileHeader(StringBuilder builder)
    {
        builder.AppendLine($"// <auto-generated by {GeneratorInfo.Name} />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("#pragma warning disable");
        builder.AppendLine();
    }

    private static void ReportDiagnostics(SourceProductionContext context, IEnumerable<DiagnosticInfo?> diagnostics)
    {
        var reported = new HashSet<(string, LocationInfo?)>();
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic is not null && reported.Add((diagnostic.Descriptor.Id, diagnostic.Location)))
            {
                context.ReportDiagnostic(diagnostic.ToDiagnostic());
            }
        }
    }

    private static string TrimGlobalPrefix(string fqn)
        => fqn.StartsWith("global::", StringComparison.Ordinal) ? fqn.Substring("global::".Length) : fqn;

    private static string Pad(int indent) => new(' ', indent * 4);
}
