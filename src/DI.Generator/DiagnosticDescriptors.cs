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
}
