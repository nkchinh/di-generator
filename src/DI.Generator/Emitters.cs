using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace NkChinh.DI.Generator;

internal static class Emitters
{
    private const string ServiceCollectionFqn = "global::Microsoft.Extensions.DependencyInjection.IServiceCollection";
    public const string RegistrationsHintName = "ServiceCollectionExtensions.g.cs";

    // --- Registrations -------------------------------------------------------

    // Framework-types-only tuple: identical across every assembly, unlike a project-embedded class or enum,
    // so passing it as a Collect{Assembly}Services parameter works safely across project references.
    private const string DescriptorTupleFqn =
        "(global::System.Type ServiceType, global::System.Type ImplementationType, int Lifetime, string? Key, bool IsHostedService, global::System.Func<global::System.IServiceProvider, object>? Factory)";
    private const string DescriptorListFqn = "global::System.Collections.Generic.ICollection<" + DescriptorTupleFqn + ">";

    public static void EmitRegistrations(
        SourceProductionContext context,
        EquatableArray<ServiceResult> results,
        string assemblyName,
        EquatableArray<ModuleInfo> modules,
        ExternalScopeRules externalScopeRules,
        bool hasMedi,
        IReadOnlyDictionary<string, (bool HasUserCtor, EquatableArray<InjectMemberInfo> Members)>? injectMeta)
    {
        ReportDiagnostics(context, results.Select(static r => r.Diagnostic));
        ReportDiagnostics(context, externalScopeRules.Diagnostics.Select(static d => (DiagnosticInfo?)d));

        var externalScopeByType = externalScopeRules.Rules.ToDictionary(
            static r => r.TypeFqn, static r => r.Lifetime, StringComparer.Ordinal);

        var services = ResolveValidServices(context, results, externalScopeByType);
        EmitInjectDiagnostics(context, services, injectMeta, hasMedi);
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
                builder.AppendLine("            " + FormatDescriptor(context, service, injectMeta));
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

    private static string FormatDescriptor(
        SourceProductionContext context,
        ServiceInfo service,
        IReadOnlyDictionary<string, (bool HasUserCtor, EquatableArray<InjectMemberInfo> Members)>? injectMeta)
    {
        var target = service.ServiceFqn ?? service.ImplementationFqn;
        var lifetime = $"(int)global::DIGen.DiServiceScope.{service.Lifetime}";
        var keyArg = service.Key is null ? "null" : SymbolDisplay.FormatLiteral(service.Key, quote: true);

        if (service.IsHostedService)
        {
            return $"registrations.Add((typeof(global::Microsoft.Extensions.Hosting.IHostedService), typeof({service.ImplementationFqn}), {lifetime}, null, true, null));";
        }

        // Check if this service has [Inject] members + user constructors → needs factory
        if (injectMeta is not null &&
            injectMeta.TryGetValue(service.ImplementationFqn, out var meta) &&
            meta.HasUserCtor &&
            meta.Members.Count > 0)
        {
            return FormatFactoryDescriptor(target, service.ImplementationFqn, lifetime, keyArg, meta.Members);
        }

        return $"registrations.Add((typeof({target}), typeof({service.ImplementationFqn}), {lifetime}, {keyArg}, false, null));";
    }

    private static string FormatFactoryDescriptor(
        string targetFqn,
        string implFqn,
        string lifetime,
        string keyArg,
        EquatableArray<InjectMemberInfo> members)
    {
        var paramArgs = new List<string>();
        for (var i = 0; i < members.Count; i++)
        {
            var m = members[i];
            if (m.IsOptional)
            {
                paramArgs.Add($"global::DIGen.InjectServiceResolver.GetOptional<{m.TypeFqn}>(sp)");
            }
            else
            {
                paramArgs.Add($"global::DIGen.InjectServiceResolver.GetRequired<{m.TypeFqn}>(sp)");
            }
        }

        var factoryBody = $"sp => new {implFqn}({string.Join(", ", paramArgs)})";

        // Pad the factory lambda nicely when it's long
        var factoryArg = factoryBody.Length > 80
            ? factoryBody
            : factoryBody;

        return $"registrations.Add((typeof({targetFqn}), typeof({implFqn}), {lifetime}, {keyArg}, false, {factoryArg}));";
    }

    // --- [Inject] constructors -----------------------------------------------

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

    private static string EmitConstructor(InjectClassShell shell, List<InjectMemberInfo> members)
    {
        var parameterNames = NameHelper.AssignParameterNames(
            [.. members.Select(static m => (m.TypeShortName, m.MemberName))]);

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

    // --- Helpers -------------------------------------------------------------

    /// <summary>Builds a lookup of inject constructor metadata from inject pipeline results.</summary>
    public static Dictionary<string, (bool HasUserCtor, EquatableArray<InjectMemberInfo> Members)> BuildInjectMeta(
        IReadOnlyList<InjectResult> results)
    {
        var groups = new Dictionary<string, (bool HasUserCtor, List<InjectMemberInfo> Members)>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            if (result.Shell is null || result.Member is null || result.Diagnostic is not null ||
                result.GroupKey is null)
            {
                continue;
            }

            if (!groups.ContainsKey(result.GroupKey))
            {
                groups[result.GroupKey] = (result.Shell.HasUserConstructor, new List<InjectMemberInfo>());
            }

            groups[result.GroupKey].Members.Add(result.Member);
        }

        return groups.ToDictionary(
            static kv => kv.Key,
            static kv => (kv.Value.HasUserCtor, new EquatableArray<InjectMemberInfo>(kv.Value.Members.ToArray())),
            StringComparer.Ordinal);
    }

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

    private static void EmitInjectDiagnostics(
        SourceProductionContext context,
        List<ServiceInfo> services,
        IReadOnlyDictionary<string, (bool HasUserCtor, EquatableArray<InjectMemberInfo> Members)>? injectMeta,
        bool hasMedi)
    {
        if (injectMeta is null || injectMeta.Count == 0)
        {
            return;
        }

        var registered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in services)
        {
            registered.Add(s.ServiceFqn ?? s.ImplementationFqn);
        }

        var registeredImpls = new HashSet<string>(
            services.Select(static s => s.ImplementationFqn),
            StringComparer.Ordinal);

        foreach (var entry in injectMeta)
        {
            var trimmedClass = TrimGlobalPrefix(entry.Key);
            var hasUserCtor = entry.Value.HasUserCtor;
            var classFqn = entry.Key;

            foreach (var m in entry.Value.Members)
            {
                // DIGEN012: keyed [Inject] in a project without MEDI
                if (m.Key is not null && !hasMedi)
                {
                    context.ReportDiagnostic(
                        DiagnosticInfo.Create(
                            DiagnosticDescriptors.InjectKeyedWithoutMedi,
                            m.Location,
                            trimmedClass,
                            m.MemberName,
                            m.Key).ToDiagnostic());
                }

                // DIGEN011: non-optional [Inject] member whose type is not registered
                // Only when the class has a user constructor (factory delegate path used)
                // AND the class itself is registered as a service in this assembly.
                if (hasUserCtor && !m.IsOptional &&
                    registeredImpls.Contains(classFqn) &&
                    !registered.Contains(m.TypeFqn))
                {
                    context.ReportDiagnostic(
                        DiagnosticInfo.Create(
                            DiagnosticDescriptors.InjectConstructorUnresolvable,
                            m.Location,
                            trimmedClass,
                            m.MemberName,
                            TrimGlobalPrefix(m.TypeFqn)).ToDiagnostic());
                }
            }
        }
    }

    private static string TrimGlobalPrefix(string fqn)
        => fqn.StartsWith("global::", StringComparison.Ordinal) ? fqn.Substring("global::".Length) : fqn;

    private static string Pad(int indent) => new(' ', indent * 4);
}