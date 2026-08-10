using Microsoft.CodeAnalysis;

namespace NkChinh.DI.Generator;

internal static class DiagnosticDescriptors
{
    private const string Category = "NkChinh.DI.Generator";
    private const string HelpBase = "https://github.com/nkchinh/di-generator/blob/main/docs/diagnostics.md";

    public static readonly DiagnosticDescriptor ServiceTypeNotImplemented = new(
        id: "DIGEN001",
        title: "Service type not implemented",
        messageFormat: "Class '{0}' cannot be registered as service type '{1}' because it does not implement or inherit it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generic parameter of a service lifetime attribute must be an interface the class implements or a base type it inherits.",
        helpLinkUri: HelpBase + "#digen001");

    public static readonly DiagnosticDescriptor InjectTypeNotPartial = new(
        id: "DIGEN002",
        title: "[Inject] containing type must be partial",
        messageFormat: "Type '{0}' must be declared 'partial' (including all containing types) because it contains [Inject] members",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generator adds a constructor in a separate partial declaration, which requires the type (and every containing type) to be partial.",
        helpLinkUri: HelpBase + "#digen002");

    public static readonly DiagnosticDescriptor InjectMemberIsStatic = new(
        id: "DIGEN003",
        title: "[Inject] member must be an instance member",
        messageFormat: "[Inject] member '{0}.{1}' must not be static or const",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Constructor injection assigns instance state; static and const members cannot be assigned from a constructor parameter.",
        helpLinkUri: HelpBase + "#digen003");

    public static readonly DiagnosticDescriptor InjectPropertyNotAssignable = new(
        id: "DIGEN004",
        title: "[Inject] property cannot be assigned from a constructor",
        messageFormat: "[Inject] property '{0}.{1}' cannot be assigned from a constructor; make it an auto-property or add a setter",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "#digen004");

    public static readonly DiagnosticDescriptor AbstractClassSkipped = new(
        id: "DIGEN005",
        title: "Abstract class cannot be registered as a service",
        messageFormat: "Abstract class '{0}' cannot be registered as a service; the attribute is ignored",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "#digen005");

    public static readonly DiagnosticDescriptor MultipleLifetimeAttributes = new(
        id: "DIGEN006",
        title: "Multiple service lifetime attributes",
        messageFormat: "Class '{0}' has multiple service lifetime attributes; apply exactly one of [SingletonService], [ScopedService], or [TransientService]",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "#digen006");

    public static readonly DiagnosticDescriptor InjectNotInClass = new(
        id: "DIGEN007",
        title: "[Inject] is only supported inside classes",
        messageFormat: "[Inject] is only supported inside classes; '{0}' is not a class",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "#digen007");

    public static readonly DiagnosticDescriptor ServiceAttributeRequiresLockedScope = new(
        id: "DIGEN008",
        title: "[Service<T>] requires a locked scope",
        messageFormat:
            "Class '{0}' uses [Service<{1}>] but '{1}' has no locked scope; add [RequiredScope] to '{1}' " +
            "(or an [assembly: RequiredExternalScope] declaration for it), or use an explicit " +
            "[SingletonService<T>]/[ScopedService<T>]/[TransientService<T>] instead",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[Service<T>] resolves its lifetime from T's locked scope; there is nothing to resolve it from.",
        helpLinkUri: HelpBase + "#digen008");

    public static readonly DiagnosticDescriptor LifetimeDisagreesWithLockedScope = new(
        id: "DIGEN009",
        title: "Lifetime attribute disagrees with the locked scope",
        messageFormat: "Class '{0}' is registered with lifetime '{2}' but '{1}' is locked to '{3}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A [RequiredScope] or [assembly: RequiredExternalScope] lock on the service type must match the registration's lifetime.",
        helpLinkUri: HelpBase + "#digen009");

    public static readonly DiagnosticDescriptor ConflictingRequiredExternalScope = new(
        id: "DIGEN010",
        title: "Conflicting RequiredExternalScope declarations",
        messageFormat:
            "Type '{0}' is locked to different lifetimes by more than one [assembly: RequiredExternalScope] " +
            "declaration reachable from this project",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "#digen010");

    public static readonly DiagnosticDescriptor InjectConstructorUnresolvable = new(
        id: "DIGEN011",
        title: "[Inject] constructor parameter may not be resolvable from DI",
        messageFormat: "Constructor parameter '{1}' of type '{2}' in '{0}' cannot be verified as a registered service; " +
            "if not registered at runtime, the factory delegate will throw — mark the member nullable (T?) " +
            "if the dependency is optional",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "#digen011");

    public static readonly DiagnosticDescriptor ServiceTypeNotPublic = new(
        id: "DIGEN012",
        title: "Cross-assembly service types should be public",
        messageFormat: "Service '{0}' or its implementation '{1}' is not public; generated registrations from another assembly may not compile",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Services published for cross-assembly generated registration must expose both the service and implementation types, unless the consuming assembly has access through InternalsVisibleTo.",
        helpLinkUri: HelpBase + "#digen012");

    public static readonly DiagnosticDescriptor ReferencedServiceNotAccessible = new(
        id: "DIGEN013",
        title: "Referenced service cannot be accessed by generated registration",
        messageFormat: "Referenced service '{0}' from '{1}' was not registered because its service or implementation type is inaccessible from this host; make both types public or use InternalsVisibleTo",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A host cannot emit a registration for an inaccessible service definition published by a referenced assembly.",
        helpLinkUri: HelpBase + "#digen013");
}
