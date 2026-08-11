using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace NkChinh.DI.Generator;

internal static class Emitters
{
    // --- Registrations -------------------------------------------------------

    private const string ServiceCollectionFqn = "global::Microsoft.Extensions.DependencyInjection.IServiceCollection";
    public const string RegistrationsHintName = "ServiceCollectionExtensions.g.cs";
    public const string ServiceDefinitionsHintName = "ServiceDefinitions.g.cs";

    public static void EmitRegistrations(
        SourceProductionContext context,
        EquatableArray<ServiceResult> results,
        string assemblyName,
        ExternalScopeRules externalScopeRules,
        bool hasMedi,
        IReadOnlyDictionary<string, (bool HasUserCtor, EquatableArray<InjectMemberInfo> Members)>? injectMeta,
        ReferencedServices referencedServices)
    {
        var publishedDefinitions = referencedServices.Definitions;
        var moduleIdentifiers = referencedServices.RegistrationModuleIdentifiers;
        bool IsModuleDefinition(ServiceDefinitionData definition) =>
            moduleIdentifiers.Contains(definition.OwnerAssemblyName);

        ReportDiagnostics(context, results.Select(static r => r.Diagnostic));
        ReportDiagnostics(context, externalScopeRules.Diagnostics.Select(static d => (DiagnosticInfo?)d));

        foreach (var definition in publishedDefinitions.Where(d => !IsModuleDefinition(d) && !d.IsAccessibleToConsumer))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ReferencedServiceNotAccessible,
                Location.None,
                definition.ServiceTypeFqn,
                definition.ImplementationTypeFqn));
        }

        publishedDefinitions = new EquatableArray<ServiceDefinitionData>(
            publishedDefinitions.Where(d => IsModuleDefinition(d) || d.IsAccessibleToConsumer).ToArray());

        var externalScopeByType = externalScopeRules.Rules.ToDictionary(
            static r => r.TypeFqn, static r => r.Lifetime, StringComparer.Ordinal);

        var services = ResolveValidServices(context, results, externalScopeByType);
        var ownDefinitions = BuildOwnDefinitions(services, injectMeta, assemblyName);

        // Each project validates its own [Inject] members: the registered pool is made of its own
        // services plus every service published by a reachable referencing assembly.
        ReportResolvabilityDiagnostics(context, services, injectMeta, publishedDefinitions);

        var identifier = NameHelper.SanitizeAssemblyIdentifier(assemblyName);
        var hasOwnServices = ownDefinitions.Count > 0;

        if (hasOwnServices)
        {
            EmitServiceDefinitions(context, ownDefinitions, hasMedi, assemblyName);
        }

        if (hasMedi && (hasOwnServices || publishedDefinitions.Count > 0))
        {
            EmitAddMethods(context, ownDefinitions, publishedDefinitions, referencedServices.RegistrationModuleIdentifiers, identifier);
        }
    }

    private static List<ServiceDefinitionData> BuildOwnDefinitions(
        List<ResolvedServiceInfo> services,
        IReadOnlyDictionary<string, (bool HasUserCtor, EquatableArray<InjectMemberInfo> Members)>? injectMeta,
        string assemblyName)
    {
        var definitions = new List<ServiceDefinitionData>(services.Count);
        foreach (var service in services)
        {
            var serviceType = service.ServiceFqn ?? service.ImplementationFqn;
            EquatableArray<string> names = EquatableArray<string>.Empty;
            EquatableArray<string> types = EquatableArray<string>.Empty;
            EquatableArray<string> keys = EquatableArray<string>.Empty;
            EquatableArray<bool> optionals = EquatableArray<bool>.Empty;
            var requiresFactory = false;

            if (injectMeta is not null &&
                injectMeta.TryGetValue(service.ImplementationFqn, out var meta) &&
                meta.Members.Count > 0)
            {
                // Members must be published in the same order the generated constructor uses.
                var members = meta.Members
                    .OrderBy(static m => m.FilePath, StringComparer.Ordinal)
                    .ThenBy(static m => m.SpanStart)
                    .ToArray();
                names = new EquatableArray<string>(members.Select(static m => m.MemberName).ToArray());
                types = new EquatableArray<string>(members.Select(static m => NonNullable(m.TypeFqn)).ToArray());
                keys = new EquatableArray<string>(members.Select(static m => m.Key ?? string.Empty).ToArray());
                optionals = new EquatableArray<bool>(members.Select(static m => m.IsOptional).ToArray());
                requiresFactory = meta.HasUserCtor || members.Any(static m => m.Key is not null || m.IsOptional);
            }

            definitions.Add(new ServiceDefinitionData(
                assemblyName,
                serviceType,
                service.ImplementationFqn,
                service.Lifetime,
                service.Key,
                service.IsHostedService,
                requiresFactory,
                 names,
                 types,
                 keys,
                 optionals,
                 true));
        }

        return definitions;
    }

    /// <summary>Publishes this assembly's service definitions as assembly-level attributes that a
    /// referencing MEDI host can consume. Works without any MEDI reference.</summary>
    private static void EmitServiceDefinitions(
        SourceProductionContext context,
        List<ServiceDefinitionData> definitions,
        bool hasMedi,
        string assemblyName)
    {
        var builder = new StringBuilder();
        AppendFileHeader(builder);
        builder.AppendLine();

        if (hasMedi)
        {
            builder.AppendLine($"[assembly: global::DIGen.Generated.RegistrationModule({SymbolDisplay.FormatLiteral(assemblyName, quote: true)})]");
            builder.AppendLine();
        }

        foreach (var d in definitions)
        {
            builder.AppendLine("[assembly: global::DIGen.Generated.ServiceDefinition(");
            builder.AppendLine($"    typeof({d.ImplementationTypeFqn}),");
            builder.AppendLine($"    typeof({d.ServiceTypeFqn}),");
            builder.AppendLine($"    (int)global::DIGen.DiServiceScope.{d.Lifetime},");
            builder.AppendLine($"    {FormatKey(d.Key)},");
            builder.AppendLine($"    {(d.IsHostedService ? "true" : "false")},");
            builder.AppendLine($"    {(d.RequiresFactory ? "true" : "false")},");
            builder.AppendLine($"    new string[] {{ {string.Join(", ", d.MemberNames.Select(static n => SymbolDisplay.FormatLiteral(n, quote: true)))} }},");
            builder.AppendLine($"    new global::System.Type[] {{ {string.Join(", ", d.MemberTypeFqns.Select(static t => "typeof(" + t + ")"))} }},");
            builder.AppendLine($"    new string[] {{ {string.Join(", ", d.MemberKeys.Select(static k => k.Length == 0 ? "\"\"" : SymbolDisplay.FormatLiteral(k, quote: true)))} }},");
            builder.AppendLine($"    new bool[] {{ {string.Join(", ", d.MemberOptionals.Select(static o => o ? "true" : "false"))} }})]");
            builder.AppendLine();
        }

        // Keep the generated file stable without an extra blank line after the last attribute.
        builder.Length--;
        if (builder[builder.Length - 1] == '\r')
        {
            builder.Length--;
        }

        context.AddSource(ServiceDefinitionsHintName, SourceText.From(builder.ToString(), Encoding.UTF8));
    }

    private static void EmitAddMethods(
        SourceProductionContext context,
        List<ServiceDefinitionData> ownDefinitions,
        EquatableArray<ServiceDefinitionData> publishedDefinitions,
        EquatableArray<string> moduleIdentifiers,
        string identifier)
    {
        var className = identifier + "ServiceCollectionExtensions";
        var addMethodName = "Add" + identifier + "Services";
        var addOwnedMethodName = "Add" + identifier + "OwnedServices";

        var owned = ownDefinitions
            .OrderBy(static d => d.ImplementationTypeFqn, StringComparer.Ordinal)
            .ThenBy(static d => d.ServiceTypeFqn, StringComparer.Ordinal)
            .ThenBy(static d => d.Key, StringComparer.Ordinal)
            .ToList();

        var builder = new StringBuilder();
        AppendFileHeader(builder);
        builder.AppendLine();
        builder.AppendLine("namespace Microsoft.Extensions.DependencyInjection");
        builder.AppendLine("{");
        builder.AppendLine($"    [global::System.CodeDom.Compiler.GeneratedCode(\"{GeneratorInfo.Name}\", \"{GeneratorInfo.Version}\")]");
        builder.AppendLine($"    public static class {className}");
        builder.AppendLine("    {");
        builder.AppendLine("        /// <summary>Registers services owned by this assembly.</summary>");
        builder.AppendLine($"        public static {ServiceCollectionFqn} {addOwnedMethodName}(this {ServiceCollectionFqn} services)");
        builder.AppendLine("        {");
        foreach (var d in owned)
        {
            builder.AppendLine("            " + FormatRegistration(d));
        }

        builder.AppendLine("            return services;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        /// <summary>Registers this assembly's services and all reachable dependencies.</summary>");
        builder.AppendLine($"        public static {ServiceCollectionFqn} {addMethodName}(this {ServiceCollectionFqn} services)");
        builder.AppendLine("        {");
        foreach (var moduleAssemblyName in moduleIdentifiers)
        {
            var moduleIdentifier = NameHelper.SanitizeAssemblyIdentifier(moduleAssemblyName);
            builder.AppendLine($"            global::Microsoft.Extensions.DependencyInjection.{moduleIdentifier}ServiceCollectionExtensions.Add{moduleIdentifier}OwnedServices(services);");
        }

        var nonModuleDefinitions = publishedDefinitions
            .Where(d => !moduleIdentifiers.Contains(d.OwnerAssemblyName))
            .Distinct()
            .OrderBy(static d => d.ImplementationTypeFqn, StringComparer.Ordinal)
            .ThenBy(static d => d.ServiceTypeFqn, StringComparer.Ordinal)
            .ThenBy(static d => d.Key, StringComparer.Ordinal);
        foreach (var d in nonModuleDefinitions)
        {
            builder.AppendLine("            " + FormatRegistration(d));
        }

        builder.AppendLine($"            {addOwnedMethodName}(services);");
        builder.AppendLine("            return services;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");

        context.AddSource(RegistrationsHintName, SourceText.From(builder.ToString(), Encoding.UTF8));
    }

    private static string FormatRegistration(ServiceDefinitionData d)
    {
        var lifetime = d.Lifetime;
        var hostNamespace = "global::Microsoft.Extensions.DependencyInjection";

        if (d.IsHostedService)
        {
            var hostedServiceType = "global::Microsoft.Extensions.Hosting.IHostedService";
            var descriptor = d.RequiresFactory
                ? $"{hostNamespace}.ServiceDescriptor.Singleton<{hostedServiceType}>(sp => new {d.ImplementationTypeFqn}({FormatFactoryArgs(d)}))"
                : $"{hostNamespace}.ServiceDescriptor.Singleton<{hostedServiceType}, {d.ImplementationTypeFqn}>()";
            return $"{hostNamespace}.Extensions.ServiceCollectionDescriptorExtensions.TryAddEnumerable(services, {descriptor});";
        }

        var keyLiteral = FormatKey(d.Key);
        var isSelf = d.ServiceTypeFqn == d.ImplementationTypeFqn;

        if (d.RequiresFactory)
        {
            var factoryArgs = FormatFactoryArgs(d);
            if (d.Key is null)
            {
                return $"services.Add{lifetime}<{d.ServiceTypeFqn}>(sp => new {d.ImplementationTypeFqn}({factoryArgs}));";
            }

            return $"services.AddKeyed{lifetime}<{d.ServiceTypeFqn}>({keyLiteral}, (sp, key) => new {d.ImplementationTypeFqn}({factoryArgs}));";
        }

        if (d.Key is null)
        {
            return isSelf
                ? $"services.Add{lifetime}<{d.ImplementationTypeFqn}>();"
                : $"services.Add{lifetime}<{d.ServiceTypeFqn}, {d.ImplementationTypeFqn}>();";
        }

        return isSelf
            ? $"services.AddKeyed{lifetime}<{d.ImplementationTypeFqn}>({keyLiteral});"
            : $"services.AddKeyed{lifetime}<{d.ServiceTypeFqn}, {d.ImplementationTypeFqn}>({keyLiteral});";
    }

    private static string FormatFactoryArgs(ServiceDefinitionData d)
    {
        var args = new List<string>(d.MemberNames.Count);
        for (var i = 0; i < d.MemberNames.Count; i++)
        {
            var type = d.MemberTypeFqns[i];
            var key = d.MemberKeys[i];
            var isOptional = d.MemberOptionals[i];

            if (key.Length == 0)
            {
                args.Add(isOptional
                    ? $"global::DIGen.InjectServiceResolver.GetOptional<{type}>(sp)"
                    : $"global::DIGen.InjectServiceResolver.GetRequired<{type}>(sp)");
            }
            else
            {
                var keyLiteral = SymbolDisplay.FormatLiteral(key, quote: true);
                args.Add(isOptional
                    ? $"global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions.GetKeyedService<{type}>(sp, {keyLiteral})"
                    : $"global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions.GetRequiredKeyedService<{type}>(sp, {keyLiteral})");
            }
        }

        return string.Join(", ", args);
    }

    private static string FormatKey(string? key)
        => key is null ? "null" : SymbolDisplay.FormatLiteral(key, quote: true);

    private static string NonNullable(string fqn)
        => fqn.EndsWith("?", StringComparison.Ordinal) ? fqn.Substring(0, fqn.Length - 1) : fqn;

    private static List<ResolvedServiceInfo> ResolveValidServices(
        SourceProductionContext context,
        EquatableArray<ServiceResult> results,
        IReadOnlyDictionary<string, string> externalScopeByType)
    {
        var valid = new List<ResolvedServiceInfo>();
        var scopeResolved = ResolveLockedScopes(
            context,
            results.Select(static r => r.Service).OfType<ServiceInfo>(),
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

    private static List<ResolvedServiceInfo> ResolveLockedScopes(
        SourceProductionContext context,
        IEnumerable<ServiceInfo> services,
        IReadOnlyDictionary<string, string> externalScopeByType)
    {
        var resolved = new List<ResolvedServiceInfo>();
        foreach (var service in services)
        {
            if (service.ServiceFqn is null)
            {
                if (service.Lifetime is { } selfLifetime)
                {
                    resolved.Add(ToResolved(service, selfLifetime));
                }

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

                resolved.Add(ToResolved(service, locked));
                continue;
            }

            if (locked is not null && service.Lifetime is { } declaredLifetime && locked != declaredLifetime)
            {
                context.ReportDiagnostic(
                    DiagnosticInfo.Create(
                        DiagnosticDescriptors.LifetimeDisagreesWithLockedScope,
                        service.Location,
                        TrimGlobalPrefix(service.ImplementationFqn),
                        TrimGlobalPrefix(service.ServiceFqn),
                        declaredLifetime,
                        locked).ToDiagnostic());
                continue;
            }

            if (service.Lifetime is { } lifetime)
            {
                resolved.Add(ToResolved(service, lifetime));
            }
        }

        return resolved;
    }

    private static ResolvedServiceInfo ToResolved(ServiceInfo service, string lifetime)
        => new(
            service.ImplementationFqn,
            service.ServiceFqn,
            lifetime,
            service.Key,
            service.IsHostedService,
            service.Location);

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
                .Select(static r => r.GroupKey)
                .OfType<string>(),
            StringComparer.Ordinal);

        var groups = GetValidInjectResults(results)
            .GroupBy(static r => r.Shell.GroupKey, StringComparer.Ordinal)
            .OrderBy(static g => g.Key, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            if (failedGroups.Contains(group.Key))
            {
                continue;
            }

            var shell = group.First().Shell;
            var members = group
                .Select(static r => r.Member)
                .Distinct()
                .OrderBy(static m => m.FilePath, StringComparer.Ordinal)
                .ThenBy(static m => m.SpanStart)
                .ToList();

            context.AddSource(shell.HintName, SourceText.From(EmitConstructor(shell, members), Encoding.UTF8));
        }
    }

    private static IEnumerable<(InjectClassShell Shell, InjectMemberInfo Member)> GetValidInjectResults(
        IEnumerable<InjectResult> results)
    {
        foreach (var result in results)
        {
            if (result.Shell is { } shell && result.Member is { } member)
            {
                yield return (shell, member);
            }
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

    // DIGEN011: each project validates its own [Inject] members. The "registered" pool is this
    // assembly's own services plus every service published by a reachable referencing assembly, so
    // a member backed by a service registered in a project you already reference is not flagged.
    private static void ReportResolvabilityDiagnostics(
        SourceProductionContext context,
        List<ResolvedServiceInfo> services,
        IReadOnlyDictionary<string, (bool HasUserCtor, EquatableArray<InjectMemberInfo> Members)>? injectMeta,
        EquatableArray<ServiceDefinitionData> publishedDefinitions)
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

        foreach (var d in publishedDefinitions)
        {
            registered.Add(d.ServiceTypeFqn);
            registered.Add(d.ImplementationTypeFqn);
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
                // Non-optional [Inject] member whose type is not registered anywhere reachable.
                // Only when the class has a user constructor (factory delegate path used) and the
                // class itself is registered as a service in this assembly.
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
