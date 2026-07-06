using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace VibeTuner.App.Generators;

[Generator]
public class ServiceCollectionGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. Lấy RootNamespace từ Project Property
        var rootNamespaceProvider = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) =>
            {
                options.GlobalOptions.TryGetValue("build_property.Generator_RootNamespace", out var gns);
                options.GlobalOptions.TryGetValue("build_property.RootNamespace", out var rns);
                return gns ?? rns ?? "VibeTuner";
            });

        // 2. Sinh các Attribute dựa trên RootNamespace
        context.RegisterSourceOutput(rootNamespaceProvider, static (spc, ns) =>
        {
            spc.AddSource("ServiceLifetimeAttributes.g.cs", $$"""
                #nullable enable

                using System;

                namespace {{ns}}.Attributes;

                /// <summary>
                /// Marks a class to be registered as a Singleton service.
                /// The generic parameter specifies the service interface type.
                /// </summary>
                [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
                internal sealed class SingletonServiceAttribute<TService> : Attribute where TService : class
                {
                    public string? Key { get; }

                    public SingletonServiceAttribute() { }

                    public SingletonServiceAttribute(string key)
                    {
                        Key = key;
                    }
                }

                /// <summary>
                /// Marks a class to be registered as a Singleton service (self-registration).
                /// </summary>
                [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
                internal sealed class SingletonServiceAttribute : Attribute
                {
                    public string? Key { get; }

                    public SingletonServiceAttribute() { }

                    public SingletonServiceAttribute(string key)
                    {
                        Key = key;
                    }
                }

                /// <summary>
                /// Marks a class to be registered as a Scoped service.
                /// The generic parameter specifies the service interface type.
                /// </summary>
                [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
                internal sealed class ScopedServiceAttribute<TService> : Attribute where TService : class
                {
                    public string? Key { get; }

                    public ScopedServiceAttribute() { }

                    public ScopedServiceAttribute(string key)
                    {
                        Key = key;
                    }
                }

                /// <summary>
                /// Marks a class to be registered as a Scoped service (self-registration).
                /// </summary>
                [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
                internal sealed class ScopedServiceAttribute : Attribute
                {
                    public string? Key { get; }

                    public ScopedServiceAttribute() { }

                    public ScopedServiceAttribute(string key)
                    {
                        Key = key;
                    }
                }

                /// <summary>
                /// Marks a class to be registered as a Transient service.
                /// The generic parameter specifies the service interface type.
                /// </summary>
                [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
                internal sealed class TransientServiceAttribute<TService> : Attribute where TService : class
                {
                    public string? Key { get; }

                    public TransientServiceAttribute() { }

                    public TransientServiceAttribute(string key)
                    {
                        Key = key;
                    }
                }

                /// <summary>
                /// Marks a class to be registered as a Transient service (self-registration).
                /// </summary>
                [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
                internal sealed class TransientServiceAttribute : Attribute
                {
                    public string? Key { get; }

                    public TransientServiceAttribute() { }

                    public TransientServiceAttribute(string key)
                    {
                        Key = key;
                    }
                }
                """);
        });

        // 3. Quét các class có gắn service attributes
        var serviceDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => s is ClassDeclarationSyntax c && c.AttributeLists.Count > 0,
                transform: static (ctx, _) => GetServiceSymbol(ctx))
            .Where(static m => m is not null);

        // 4. Kết hợp: List Services + RootNamespace
        var combined = serviceDeclarations.Collect().Combine(rootNamespaceProvider);

        // 5. Sinh code đăng ký services
        context.RegisterSourceOutput(combined, static (spc, source) =>
        {
            var symbols = source.Left;
            var rootNamespace = source.Right;

            if (symbols.IsDefaultOrEmpty) return;

            var sb = new StringBuilder();
            sb.AppendLine($$"""
                #nullable enable

                using System;
                using Microsoft.Extensions.DependencyInjection;
                using Microsoft.Extensions.Hosting;

                namespace {{rootNamespace}};

                public static partial class ServiceCollectionExtensions
                {
                    public static IServiceCollection AddRegisteredServices(this IServiceCollection services)
                    {
                """);

            var displayFormat = new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

            foreach (var symbol in symbols)
            {
                var (lifetime, key, serviceType) = GetLifetimeKeyAndServiceType(symbol!);
                var implType = symbol!.ToDisplayString(displayFormat);
                var interfaces = symbol.Interfaces;

                // Check if it's a hosted service
                if (interfaces.Any(i => i.ToDisplayString() == "Microsoft.Extensions.Hosting.IHostedService"))
                {
                    sb.AppendLine($"        services.AddHostedService<{implType}>();");
                }
                else
                {
                    // Determine target type
                    string target;
                    if (serviceType != null)
                    {
                        // Generic attribute was used - validate implementation
                        if (!ImplementsInterface(symbol, serviceType))
                        {
                            spc.ReportDiagnostic(Diagnostic.Create(
                                new DiagnosticDescriptor(
                                    "VBSVC001",
                                    "Service implementation mismatch",
                                    "Class '{0}' does not implement interface '{1}' specified in service attribute",
                                    "ServiceRegistration",
                                    DiagnosticSeverity.Error,
                                    isEnabledByDefault: true),
                                symbol.Locations.FirstOrDefault(),
                                implType,
                                serviceType.ToDisplayString(displayFormat)));
                            continue;
                        }
                        target = serviceType.ToDisplayString(displayFormat);
                    }
                    else
                    {
                        // Non-generic attribute - register as self (concrete type)
                        target = implType;
                    }

                    if (string.IsNullOrWhiteSpace(key))
                    {
                        sb.AppendLine(target == implType
                            ? $"        services.Add{lifetime}<{implType}>();"
                            : $"        services.Add{lifetime}<{target}, {implType}>();");
                    }
                    else
                    {
                        sb.AppendLine(target == implType
                            ? $"        services.AddKeyed{lifetime}<{implType}>(\"{key}\");"
                            : $"        services.AddKeyed{lifetime}<{target}, {implType}>(\"{key}\");");
                    }
                }
            }

            sb.AppendLine("        return services;\n    }\n}");
            spc.AddSource("ServiceCollectionExtensions.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        });
    }

    /// <summary>
    /// Lấy thông tin của class có gắn service lifetime attribute.
    /// </summary>
    private static INamedTypeSymbol? GetServiceSymbol(GeneratorSyntaxContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;

        // Kiểm tra Syntax để bắt được [SingletonService], [ScopedService], hoặc [TransientService]
        // Phải xử lý cả generic name (SingletonService<T>) và simple name (SingletonService)
        var hasAttr = classDecl.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(a =>
            {
                // Lấy base name (bỏ generic part nếu có)
                var name = a.Name switch
                {
                    GenericNameSyntax gns => gns.Identifier.Text,
                    SimpleNameSyntax sns => sns.Identifier.Text,
                    _ => a.Name.ToString()
                };

                return name is "SingletonService" or "SingletonServiceAttribute"
                    or "ScopedService" or "ScopedServiceAttribute"
                    or "TransientService" or "TransientServiceAttribute";
            });

        if (!hasAttr) return null;

        return (context.SemanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol symbol || symbol.IsAbstract)
            ? null : symbol;
    }

    /// <summary>
    /// Kiểm tra xem class có implement interface không.
    /// </summary>
    private static bool ImplementsInterface(INamedTypeSymbol symbol, INamedTypeSymbol interfaceType)
    {
        return symbol.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, interfaceType));
    }

    /// <summary>
    /// Lấy giá trị lifetime, key và service type từ attribute.
    /// </summary>
    private static (string Lifetime, string? Key, INamedTypeSymbol? ServiceType) GetLifetimeKeyAndServiceType(INamedTypeSymbol symbol)
    {
        var attr = symbol.GetAttributes().FirstOrDefault(a =>
        {
            var name = a.AttributeClass?.Name;
            return name is "SingletonServiceAttribute" or "SingletonService"
                or "ScopedServiceAttribute" or "ScopedService"
                or "TransientServiceAttribute" or "TransientService";
        });

        if (attr == null) return ("Transient", null, null);

        var lifetime = attr.AttributeClass?.Name switch
        {
            "SingletonServiceAttribute" or "SingletonService" => "Singleton",
            "ScopedServiceAttribute" or "ScopedService" => "Scoped",
            _ => "Transient"
        };

        // Lấy service type từ generic parameter nếu có
        // Phải dùng ConstructedFrom vì AttributeClass là constructed generic type
        INamedTypeSymbol? serviceType = null;
        if (attr.AttributeClass is { IsGenericType: true })
        {
            // TypeArguments của constructed generic type chứa actual type arguments
            var typeArgs = attr.AttributeClass.TypeArguments;
            if (typeArgs.Length > 0)
            {
                serviceType = typeArgs[0] as INamedTypeSymbol;
            }
        }

        // Lấy key nếu có
        string? key = null;
        if (attr.ConstructorArguments.Length > 0)
        {
            key = attr.ConstructorArguments[0].Value?.ToString();
        }
        else
        {
            // Fallback: parse từ syntax nếu semantic chưa sẵn sàng
            var syntax = attr.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;
            if (syntax?.ArgumentList?.Arguments.Count > 0)
            {
                key = syntax.ArgumentList.Arguments[0].Expression.ToString().Trim('"');
            }
        }

        return (lifetime, key, serviceType);
    }
}