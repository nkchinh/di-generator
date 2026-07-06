using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NkChinh.DI.Generator;

internal static class Parsers
{
    private static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly SymbolDisplayFormat MessageFormat = SymbolDisplayFormat.CSharpErrorMessageFormat;

    // ---------------------------------------------------------------- services

    public static ServiceResult? GetServiceResult(GeneratorAttributeSyntaxContext context, string lifetime)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
        {
            return null;
        }

        var location = LocationInfo.CreateFrom(context.TargetNode);
        var displayName = symbol.ToDisplayString(MessageFormat);

        if (symbol.IsAbstract)
        {
            return new ServiceResult(
                null,
                DiagnosticInfo.Create(DiagnosticDescriptors.AbstractClassSkipped, location, displayName));
        }

        var attribute = context.Attributes[0];
        var key = attribute.ConstructorArguments.Length > 0
            ? attribute.ConstructorArguments[0].Value as string
            : null;

        INamedTypeSymbol? serviceType = null;
        if (attribute.AttributeClass is { IsGenericType: true } attributeClass &&
            attributeClass.TypeArguments.Length > 0)
        {
            serviceType = attributeClass.TypeArguments[0] as INamedTypeSymbol;
        }

        if (serviceType is not null && !IsAssignableTo(symbol, serviceType))
        {
            return new ServiceResult(
                null,
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.ServiceTypeNotImplemented,
                    location,
                    displayName,
                    serviceType.ToDisplayString(MessageFormat)));
        }

        var isHosted = symbol.AllInterfaces.Any(static i =>
            i.ToDisplayString() == "Microsoft.Extensions.Hosting.IHostedService");

        return new ServiceResult(
            new ServiceInfo(
                symbol.ToDisplayString(FullyQualified),
                serviceType?.ToDisplayString(FullyQualified),
                lifetime,
                key,
                isHosted,
                location),
            null);
    }

    private static bool IsAssignableTo(INamedTypeSymbol symbol, INamedTypeSymbol serviceType)
    {
        var comparer = SymbolEqualityComparer.Default;
        if (comparer.Equals(symbol, serviceType))
        {
            return true;
        }

        if (symbol.AllInterfaces.Any(i => comparer.Equals(i, serviceType)))
        {
            return true;
        }

        for (var baseType = symbol.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (comparer.Equals(baseType, serviceType))
            {
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------- [Inject]

    public static InjectResult? GetInjectResult(GeneratorAttributeSyntaxContext context)
    {
        var symbol = context.TargetSymbol;
        if (symbol.ContainingType is not { } containingType)
        {
            return null;
        }

        var groupKey = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var location = LocationInfo.CreateFrom(context.TargetNode);
        var typeDisplay = containingType.ToDisplayString(MessageFormat);

        InjectResult Fail(DiagnosticDescriptor descriptor, params string[] args)
            => new(null, null, groupKey, DiagnosticInfo.Create(descriptor, location, args));

        var isStatic = symbol switch
        {
            IFieldSymbol field => field.IsStatic || field.IsConst,
            IPropertySymbol property => property.IsStatic,
            _ => false,
        };
        if (isStatic)
        {
            return Fail(DiagnosticDescriptors.InjectMemberIsStatic, typeDisplay, symbol.Name);
        }

        if (containingType.TypeKind != TypeKind.Class)
        {
            return Fail(DiagnosticDescriptors.InjectNotInClass, typeDisplay);
        }

        var typeDeclarations = context.TargetNode.AncestorsAndSelf()
            .OfType<TypeDeclarationSyntax>()
            .ToArray(); // innermost first
        foreach (var declaration in typeDeclarations)
        {
            if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                return Fail(DiagnosticDescriptors.InjectTypeNotPartial, declaration.Identifier.Text);
            }
        }

        ITypeSymbol memberType;
        var isProperty = false;
        switch (symbol)
        {
            case IFieldSymbol field:
                memberType = field.Type;
                break;

            case IPropertySymbol property:
                isProperty = true;
                memberType = property.Type;
                if (context.TargetNode is not PropertyDeclarationSyntax propertySyntax ||
                    !IsConstructorAssignable(propertySyntax))
                {
                    return Fail(DiagnosticDescriptors.InjectPropertyNotAssignable, typeDisplay, symbol.Name);
                }

                break;

            default:
                return null;
        }

        var member = new InjectMemberInfo(
            symbol.Name,
            memberType.ToDisplayString(FullyQualified),
            (memberType as INamedTypeSymbol)?.Name ?? string.Empty,
            isProperty,
            context.TargetNode.SyntaxTree.FilePath,
            context.TargetNode.SpanStart);

        var shell = BuildShell(containingType, typeDeclarations, groupKey);
        return new InjectResult(shell, member, groupKey, null);
    }

    private static bool IsConstructorAssignable(PropertyDeclarationSyntax property)
    {
        if (property.ExpressionBody is not null)
        {
            return false;
        }

        var accessors = property.AccessorList?.Accessors;
        if (accessors is null)
        {
            return false;
        }

        var hasSetter = accessors.Value.Any(static a =>
            a.IsKind(SyntaxKind.SetAccessorDeclaration) || a.IsKind(SyntaxKind.InitAccessorDeclaration));
        if (hasSetter)
        {
            return true;
        }

        // Get-only is fine when it is an auto-property (assignable from any constructor of the type).
        var getter = accessors.Value.FirstOrDefault(static a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
        return getter is { Body: null, ExpressionBody: null };
    }

    private static InjectClassShell BuildShell(
        INamedTypeSymbol containingType,
        TypeDeclarationSyntax[] typeDeclarationsInnermostFirst,
        string groupKey)
    {
        var chain = new TypeShell[typeDeclarationsInnermostFirst.Length];
        var hintParts = new string[typeDeclarationsInnermostFirst.Length];
        for (var i = 0; i < typeDeclarationsInnermostFirst.Length; i++)
        {
            // Reverse: chain is emitted outermost-first.
            var declaration = typeDeclarationsInnermostFirst[typeDeclarationsInnermostFirst.Length - 1 - i];
            chain[i] = new TypeShell(
                GetTypeKeyword(declaration),
                declaration.Identifier.Text + (declaration.TypeParameterList?.ToString() ?? string.Empty));

            var arity = declaration.TypeParameterList?.Parameters.Count ?? 0;
            hintParts[i] = declaration.Identifier.Text + (arity > 0 ? "_" + arity : string.Empty);
        }

        var ns = containingType.ContainingNamespace is { IsGlobalNamespace: false } namespaceSymbol
            ? namespaceSymbol.ToDisplayString()
            : null;

        var hintName = (ns is null ? string.Empty : ns + ".") +
            string.Join(".", hintParts) +
            ".DependencyInjection.g.cs";

        return new InjectClassShell(
            groupKey,
            ns,
            containingType.Name,
            new EquatableArray<TypeShell>(chain),
            hintName);
    }

    private static string GetTypeKeyword(TypeDeclarationSyntax declaration) => declaration switch
    {
        RecordDeclarationSyntax record =>
            record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "record struct" : "record",
        StructDeclarationSyntax => "struct",
        InterfaceDeclarationSyntax => "interface",
        _ => "class",
    };

    // ---------------------------------------------------------------- referenced modules

    public static EquatableArray<ModuleInfo> GetReferencedModules(Compilation compilation)
    {
        var modules = new List<ModuleInfo>();
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                if (attribute.AttributeClass is { Name: "ServiceRegistrationModuleAttribute" } attributeClass &&
                    attributeClass.ContainingNamespace.ToDisplayString() == "DIGen.Generated" &&
                    attribute.ConstructorArguments.Length == 2 &&
                    attribute.ConstructorArguments[0].Value is string methodName &&
                    attribute.ConstructorArguments[1].Value is string typeName)
                {
                    modules.Add(new ModuleInfo(methodName, typeName));
                }
            }
        }

        return new EquatableArray<ModuleInfo>(
            modules
                .Distinct()
                .OrderBy(static m => m.MethodName, StringComparer.Ordinal)
                .ThenBy(static m => m.ExtensionsTypeName, StringComparer.Ordinal)
                .ToArray());
    }
}
