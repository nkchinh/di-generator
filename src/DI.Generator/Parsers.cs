using System.Collections.Immutable;
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

    /// <summary>Fully-qualified without nullable annotations, safe for <c>typeof(...)</c> argument
    /// emission and generic type arguments.</summary>
    private static readonly SymbolDisplayFormat FullyQualifiedTypeOf = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private static readonly SymbolDisplayFormat MessageFormat = SymbolDisplayFormat.CSharpErrorMessageFormat;

    // ---------------------------------------------------------------- services

    /// <summary>
    /// Parses a class annotated with a lifetime attribute.
    /// </summary>
    /// <param name="lifetime">
    /// The fixed lifetime name for an explicit attribute (Singleton/Scoped/Transient), or null for
    /// <c>[Service&lt;T&gt;]</c>, whose lifetime is resolved later from T's locked scope.
    /// </param>
    public static ServiceResult? GetServiceResult(GeneratorAttributeSyntaxContext context, string? lifetime)
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
                location,
                IsAutoScope: lifetime is null,
                LockedLifetime: serviceType is not null ? GetRequiredScopeLifetime(serviceType) : null),
            null);
    }

    private static string? GetRequiredScopeLifetime(INamedTypeSymbol serviceType)
    {
        foreach (var attribute in serviceType.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: "RequiredScopeAttribute" } attributeClass &&
                attributeClass.ContainingNamespace.ToDisplayString() == "DIGen" &&
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is int lifetimeValue)
            {
                return LifetimeName(lifetimeValue);
            }
        }

        return null;
    }

    private static string? LifetimeName(int DiServiceScopeValue) => DiServiceScopeValue switch
    {
        0 => "Singleton",
        1 => "Scoped",
        2 => "Transient",
        _ => null,
    };

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

        // Read key from [Inject("key")] if present
        var key = context.Attributes.Length > 0 &&
            context.Attributes[0].ConstructorArguments.Length > 0 &&
            context.Attributes[0].ConstructorArguments[0].Value is string keyValue
            ? keyValue
            : null;

        // In a nullable-aware project, the declared annotation is the contract. In an oblivious
        // project an initializer remains the only available signal that null is acceptable.
        var isOptional = memberType.NullableAnnotation == NullableAnnotation.Annotated;
        if (!isOptional && memberType.NullableAnnotation != NullableAnnotation.NotAnnotated &&
            context.TargetNode is VariableDeclaratorSyntax varDecl &&
            varDecl.Initializer is not null)
        {
            isOptional = true;
        }

        var member = new InjectMemberInfo(
            symbol.Name,
            memberType.ToDisplayString(FullyQualified),
            (memberType as INamedTypeSymbol)?.Name ?? string.Empty,
            isProperty,
            key,
            isOptional,
            context.TargetNode.SyntaxTree.FilePath,
            context.TargetNode.SpanStart,
            LocationInfo.CreateFrom(context.TargetNode));

        var hasUserCtor = containingType.Constructors.Any(
            static c => !c.IsStatic && !c.IsImplicitlyDeclared);
        var shell = BuildShell(containingType, typeDeclarations, groupKey, hasUserCtor);
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
        string groupKey,
        bool hasUserConstructor = false)
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
            hasUserConstructor,
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

    // ---------------------------------------------------------------- published definitions

    /// <summary>
    /// Reads the <c>[assembly: ServiceDefinition]</c> attributes published by every referenced assembly.
    /// Host projects combine these with their own services to emit direct registrations; the set of
    /// published service type names is additionally used as the cross-assembly "registered" pool when
    /// validating <c>[Inject]</c> members (DIGEN011).
    /// </summary>
    public static EquatableArray<ServiceDefinitionData> ReadReferencedServiceDefinitions(Compilation compilation)
    {
        var definitions = new List<ServiceDefinitionData>();
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                if (attribute.AttributeClass is { Name: "ServiceDefinitionAttribute" } attributeClass &&
                    attributeClass.ContainingNamespace.ToDisplayString() == "DIGen.Generated" &&
                    ReadServiceDefinition(attribute) is { } definition)
                {
                    definitions.Add(definition);
                }
            }
        }

        return new EquatableArray<ServiceDefinitionData>(
            definitions
                .OrderBy(static d => d.ImplementationTypeFqn, StringComparer.Ordinal)
                .ThenBy(static d => d.ServiceTypeFqn, StringComparer.Ordinal)
                .ThenBy(static d => d.Key, StringComparer.Ordinal)
                .ToArray());
    }

    private static ServiceDefinitionData? ReadServiceDefinition(AttributeData attribute)
    {
        var args = attribute.ConstructorArguments;
        if (args.Length < 10 || args[0].Value is not ITypeSymbol implementation ||
            args[2].Value is not int lifetimeValue)
        {
            return null;
        }

        var service = args[1].Value as ITypeSymbol ?? implementation;
        var key = args[3].Value as string;
        var lifetime = LifetimeName(lifetimeValue);
        if (lifetime is null)
        {
            return null;
        }

        return new ServiceDefinitionData(
            service.ToDisplayString(FullyQualifiedTypeOf),
            implementation.ToDisplayString(FullyQualifiedTypeOf),
            lifetime,
            key,
            args[4].Value is bool isHosted && isHosted,
            args[5].Value is bool requiresFactory && requiresFactory,
            new EquatableArray<string>(ReadStringValues(args[6])),
            new EquatableArray<string>(ReadTypeFqnValues(args[7])),
            new EquatableArray<string>(ReadStringValues(args[8])),
            new EquatableArray<bool>(ReadBoolValues(args[9])));
    }

    private static string[] ReadStringValues(TypedConstant constant)
    {
        if (constant.Kind != TypedConstantKind.Array)
        {
            return [];
        }

        return constant.Values.Select(static v => v.Value as string ?? string.Empty).ToArray();
    }

    private static string[] ReadTypeFqnValues(TypedConstant constant)
    {
        if (constant.Kind != TypedConstantKind.Array)
        {
            return [];
        }

        return constant.Values
            .Select(static v => (v.Value as ITypeSymbol)?.ToDisplayString(FullyQualifiedTypeOf) ?? string.Empty)
            .ToArray();
    }

    private static bool[] ReadBoolValues(TypedConstant constant)
    {
        if (constant.Kind != TypedConstantKind.Array)
        {
            return [];
        }

        return constant.Values.Select(static v => v.Value is bool value && value).ToArray();
    }

    // ---------------------------------------------------------------- external scope rules

    /// <summary>
    /// Scans this assembly and every referenced assembly for [assembly: RequiredExternalScope] declarations.
    /// Two declarations locking the same type to different lifetimes are reported as DIGEN010.
    /// </summary>
    public static ExternalScopeRules GetExternalScopeRules(Compilation compilation)
    {
        var byType = new Dictionary<string, string>(StringComparer.Ordinal);
        var conflicts = new List<DiagnosticInfo>();
        var conflictedTypes = new HashSet<string>(StringComparer.Ordinal);

        void Scan(IAssemblySymbol assembly)
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                if (attribute.AttributeClass is not { Name: "RequiredExternalScopeAttribute" } attributeClass ||
                    attributeClass.ContainingNamespace.ToDisplayString() != "DIGen" ||
                    attribute.ConstructorArguments.Length != 2 ||
                    attribute.ConstructorArguments[0].Value is not ITypeSymbol typeArgument ||
                    attribute.ConstructorArguments[1].Value is not int lifetimeValue)
                {
                    continue;
                }

                var lifetimeName = LifetimeName(lifetimeValue);
                if (lifetimeName is null)
                {
                    continue;
                }

                var typeFqn = typeArgument.ToDisplayString(FullyQualified);
                if (byType.TryGetValue(typeFqn, out var existingLifetime))
                {
                    if (existingLifetime != lifetimeName && conflictedTypes.Add(typeFqn))
                    {
                        conflicts.Add(DiagnosticInfo.Create(
                            DiagnosticDescriptors.ConflictingRequiredExternalScope,
                            null,
                            TrimGlobalPrefix(typeFqn)));
                    }
                }
                else
                {
                    byType[typeFqn] = lifetimeName;
                }
            }
        }

        Scan(compilation.Assembly);
        foreach (var referenced in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            Scan(referenced);
        }

        var rules = byType
            .Select(static kv => new ExternalScopeRule(kv.Key, kv.Value))
            .OrderBy(static r => r.TypeFqn, StringComparer.Ordinal)
            .ToArray();

        return new ExternalScopeRules(
            new EquatableArray<ExternalScopeRule>(rules),
            new EquatableArray<DiagnosticInfo>(conflicts.ToArray()));
    }

    private static string TrimGlobalPrefix(string fqn)
        => fqn.StartsWith("global::", StringComparison.Ordinal) ? fqn.Substring("global::".Length) : fqn;
}
