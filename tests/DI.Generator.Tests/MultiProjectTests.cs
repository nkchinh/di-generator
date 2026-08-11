using Microsoft.CodeAnalysis;
using Xunit;

namespace NkChinh.DI.Generator.Tests;

public class MultiProjectTests
{
    private const string Hint = "ServiceCollectionExtensions.g.cs";

    private static MetadataReference BuildLibraryReference(
        string source,
        string assemblyName,
        IEnumerable<MetadataReference>? extraReferences = null)
    {
        var outcome = GeneratorTestHelper.Run(source, assemblyName, extraReferences: extraReferences);
        Assert.Empty(outcome.CompilationErrors);
        return MetadataReference.CreateFromImage(GeneratorTestHelper.EmitAssembly(outcome.OutputCompilation));
    }

    private static MetadataReference BuildLibraryReferenceWithoutMedi(string source, string assemblyName)
    {
        var outcome = GeneratorTestHelper.Run(
            source, assemblyName, baseReferences: GeneratorTestHelper.ReferencesWithoutMedi);
        Assert.Empty(outcome.CompilationErrors);
        return MetadataReference.CreateFromImage(GeneratorTestHelper.EmitAssembly(outcome.OutputCompilation));
    }

    [Fact]
    public void Host_MergesReferencedProjectServicesIntoItsOwnMethod()
    {
        var libRef = BuildLibraryReference("""
            using DIGen;

            namespace Infra;

            public interface IRepo { }

            [ScopedService<IRepo>]
            public class Repo : IRepo { }
            """,
            assemblyName: "MyCompany.Infrastructure");

        var host = GeneratorTestHelper.Run("""
            using DIGen;

            namespace HostApp;

            [SingletonService]
            public class AppState { }
            """,
            assemblyName: "MyCompany.Host",
            extraReferences: [libRef]);

        var source = host.GetSource(Hint);
        Assert.Contains("AddMyCompanyHostServices(", source);
        Assert.Contains(
            "MyCompanyInfrastructureServiceCollectionExtensions.AddMyCompanyInfrastructureOwnedServices(services);",
            source);
        Assert.Contains(
            "services.AddSingleton<global::HostApp.AppState>();",
            source);
        Assert.Empty(host.CompilationErrors);
    }

    [Fact]
    public void Host_WithoutOwnServices_StillEmitsReferencedRegistrations()
    {
        var libRef = BuildLibraryReference("""
            using DIGen;

            namespace Infra;

            [SingletonService]
            public class Clock { }
            """,
            assemblyName: "Company.Lib");

        var host = GeneratorTestHelper.Run(
            "namespace HostApp;",
            assemblyName: "Company.Host",
            extraReferences: [libRef]);

        var source = host.GetSource(Hint);
        Assert.Contains("AddCompanyHostServices(", source);
        Assert.Contains(
            "CompanyLibServiceCollectionExtensions.AddCompanyLibOwnedServices(services);",
            source);
        Assert.Empty(host.CompilationErrors);
    }

    [Fact]
    public void Host_DelegatesInternalServicesToTheirOwningMediModule()
    {
        var libraryRef = BuildLibraryReference("""
            using DIGen;

            namespace InternalLibrary;

            internal interface IInternalService { }

            [SingletonService<IInternalService>]
            internal class InternalService : IInternalService { }
            """,
            assemblyName: "Internal.Library");

        var host = GeneratorTestHelper.Run(
            "namespace HostApp;",
            assemblyName: "Internal.Host",
            extraReferences: [libraryRef]);

        var source = host.GetSource(Hint);
        Assert.Contains(
            "InternalLibraryServiceCollectionExtensions.AddInternalLibraryOwnedServices(services);",
            source);
        Assert.DoesNotContain("services.AddSingleton<global::InternalLibrary.IInternalService", source);
        Assert.DoesNotContain(host.GeneratorDiagnostics, d => d.Id == "DIGEN013");
        Assert.Empty(host.CompilationErrors);
    }

    [Fact]
    public void ReferencedRegistrations_AppearInImplementationTypeOrder()
    {
        var alphaRef = BuildLibraryReference("""
            using DIGen;
            namespace A;
            [SingletonService] public class A1 { }
            """,
            assemblyName: "Alpha.Lib");

        var betaRef = BuildLibraryReference("""
            using DIGen;
            namespace B;
            [SingletonService] public class B1 { }
            """,
            assemblyName: "Beta.Lib");

        var host = GeneratorTestHelper.Run(
            "namespace HostApp;",
            assemblyName: "Gamma.Host",
            extraReferences: [betaRef, alphaRef]);

        var source = host.GetSource(Hint);
        var alpha = source.IndexOf(
            "AlphaLibServiceCollectionExtensions.AddAlphaLibOwnedServices(services);", StringComparison.Ordinal);
        var beta = source.IndexOf(
            "BetaLibServiceCollectionExtensions.AddBetaLibOwnedServices(services);", StringComparison.Ordinal);
        Assert.True(alpha >= 0 && beta >= 0 && alpha < beta,
            $"Expected A1 before B1. A1={alpha}, B1={beta}");
        Assert.Empty(host.CompilationErrors);
    }

    [Fact]
    public void RootRegistration_RegistersDependenciesBeforeOwnedServices()
    {
        var applicationRef = BuildLibraryReference("""
            using DIGen;

            namespace Manager.Application;

            [SingletonService]
            public class ApplicationService { }
            """,
            assemblyName: "Manager.Application");

        var infrastructureRef = BuildLibraryReference("""
            using DIGen;
            using Manager.Application;

            namespace Manager.Infrastructure;

            [SingletonService]
            public class InfrastructureService
            {
                public InfrastructureService(ApplicationService applicationService) { }
            }
            """,
            assemblyName: "Manager.Infrastructure",
            extraReferences: [applicationRef]);

        var host = GeneratorTestHelper.Run("""
            using DIGen;

            namespace DeviceManager;

            [SingletonService]
            public class HostService { }
            """,
            assemblyName: "Device.Manager",
            extraReferences: [applicationRef, infrastructureRef]);

        var source = host.GetSource(Hint);
        var application = source.IndexOf(
            "ManagerApplicationServiceCollectionExtensions.AddManagerApplicationOwnedServices(services);",
            StringComparison.Ordinal);
        var infrastructure = source.IndexOf(
            "ManagerInfrastructureServiceCollectionExtensions.AddManagerInfrastructureOwnedServices(services);",
            StringComparison.Ordinal);
        var owned = source.IndexOf("\n            AddDeviceManagerOwnedServices(services);", StringComparison.Ordinal);

        Assert.True(application >= 0 && infrastructure >= 0 && owned >= 0 &&
            application < infrastructure && infrastructure < owned,
            $"Expected dependencies before owned services. Application={application}, Infrastructure={infrastructure}, Owned={owned}");
        Assert.Empty(host.CompilationErrors);
    }

    [Fact]
    public void ProjectWithOwnServices_DoesNotEmplaceAggregator()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            [SingletonService]
            public class Clock { }
            """);

        var source = outcome.GetSource(Hint);
        Assert.Contains("AddTestAssemblyServices(", source);
        Assert.DoesNotContain("AllServices", source);
    }

    [Fact]
    public void ServiceAttribute_ResolvesRequiredScope_LockedInAReferencedProject()
    {
        var domainRef = BuildLibraryReference("""
            using DIGen;

            namespace Domain;

            [RequiredScope(DiServiceScope.Scoped)]
            public interface IOrderRepository { }
            """,
            assemblyName: "MyCompany.Domain");

        var infrastructure = GeneratorTestHelper.Run("""
            using Domain;
            using DIGen;

            namespace Infrastructure;

            [Service<IOrderRepository>]
            public class SqlOrderRepository : IOrderRepository { }
            """,
            assemblyName: "MyCompany.Infrastructure",
            extraReferences: [domainRef]);

        Assert.Empty(infrastructure.GeneratorDiagnostics);
        Assert.Contains(
            "services.AddScoped<global::Domain.IOrderRepository, global::Infrastructure.SqlOrderRepository>();",
            infrastructure.GetSource(Hint));
        Assert.Empty(infrastructure.CompilationErrors);
    }

    [Fact]
    public void RequiredExternalScope_DeclaredInOneProject_IsReachableFromAnotherReferencingIt()
    {
        var infrastructureRef = BuildLibraryReference("""
            using System;
            using DIGen;

            [assembly: RequiredExternalScope(typeof(ThirdParty.IConnection), DiServiceScope.Singleton)]

            namespace ThirdParty;

            public interface IConnection { }
            """,
            assemblyName: "MyCompany.Infrastructure");

        var host = GeneratorTestHelper.Run("""
            using ThirdParty;
            using DIGen;

            namespace HostApp;

            [Service<IConnection>]
            public class RedisConnection : IConnection { }
            """,
            assemblyName: "MyCompany.Host",
            extraReferences: [infrastructureRef]);

        Assert.Empty(host.GeneratorDiagnostics);
        Assert.Contains(
            "services.AddSingleton<global::ThirdParty.IConnection, global::HostApp.RedisConnection>();",
            host.GetSource(Hint));
        Assert.Empty(host.CompilationErrors);
    }

    [Fact]
    public void MediFreeSharedModule_RegistrationsEmittedExactlyOnce()
    {
        // Diamond: Host -> A -> Shared, Host -> B -> Shared. Shared has no MEDI reference (only
        // [RequiredScope] + [Service<T>] self-registration). The host must merge Shared's published
        // registration exactly once — never once per path that reaches it.
        var sharedRef = BuildLibraryReferenceWithoutMedi("""
            using DIGen;

            namespace Shared;

            [RequiredScope(DiServiceScope.Scoped)]
            public interface ISharedRepo { }

            [Service<ISharedRepo>]
            public class SharedRepo : ISharedRepo { }
            """,
            assemblyName: "Shared.Lib");

        var aRef = BuildLibraryReference("""
            using DIGen;
            using Shared;

            namespace A;

            [SingletonService]
            public class AThing { }
            public class AUsesShared { public SharedRepo? Value { get; set; } }
            """,
            assemblyName: "A.Lib",
            extraReferences: [sharedRef]);

        var bRef = BuildLibraryReference("""
            using DIGen;
            using Shared;

            namespace B;

            [SingletonService]
            public class BThing { }
            public class BUsesShared { public SharedRepo? Value { get; set; } }
            """,
            assemblyName: "B.Lib",
            extraReferences: [sharedRef]);

        var host = GeneratorTestHelper.Run(
            "namespace HostApp;",
            assemblyName: "Diamond.Host",
            extraReferences: [sharedRef, aRef, bRef]);

        var source = host.GetSource(Hint);
        Assert.Contains("ALibServiceCollectionExtensions.AddALibOwnedServices(services);", source);
        Assert.Contains("BLibServiceCollectionExtensions.AddBLibOwnedServices(services);", source);
        var occurrences = System.Text.RegularExpressions.Regex.Matches(
            source, "AddScoped<global::Shared.ISharedRepo, global::Shared.SharedRepo>").Count;
        Assert.Equal(1, occurrences);
        Assert.Empty(host.CompilationErrors);
    }

    [Fact]
    public void MediFreeLibrary_PublishesFactoryMetadataForHost()
    {
        var libraryRef = BuildLibraryReferenceWithoutMedi("""
            using DIGen;

            namespace Shared;

            public interface IFormatter { }
            public interface IOptionalDependency { }
            public interface IConsumer { }

            [SingletonService<IFormatter>("primary")]
            public class PrimaryFormatter : IFormatter { }

            [SingletonService<IConsumer>]
            public partial class Consumer : IConsumer
            {
                [Inject("primary")] private readonly IFormatter _formatter;
                [Inject] private readonly IOptionalDependency? _optional = null;
            }
            """,
            assemblyName: "Shared.Factory");

        var host = GeneratorTestHelper.Run(
            "namespace HostApp;",
            assemblyName: "Factory.Host",
            extraReferences: [libraryRef]);

        var source = host.GetSource(Hint);
        Assert.Contains(
            "services.AddKeyedSingleton<global::Shared.IFormatter, global::Shared.PrimaryFormatter>(\"primary\");",
            source);
        Assert.Contains(
            "services.AddSingleton<global::Shared.IConsumer>(sp => new global::Shared.Consumer(",
            source);
        Assert.Contains(
            "GetRequiredKeyedService<global::Shared.IFormatter>(sp, \"primary\")",
            source);
        Assert.Contains(
            "GetOptional<global::Shared.IOptionalDependency>(sp)",
            source);
        Assert.Empty(host.GeneratorDiagnostics);
        Assert.Empty(host.CompilationErrors);
    }
}
