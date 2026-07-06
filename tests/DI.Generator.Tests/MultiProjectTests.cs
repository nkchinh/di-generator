using Microsoft.CodeAnalysis;
using Xunit;

namespace NkChinh.DI.Generator.Tests;

public class MultiProjectTests
{
    private const string Hint = "ServiceCollectionExtensions.g.cs";

    private static MetadataReference BuildLibraryReference(string source, string assemblyName)
    {
        var outcome = GeneratorTestHelper.Run(source, assemblyName);
        Assert.Empty(outcome.CompilationErrors);
        return MetadataReference.CreateFromImage(GeneratorTestHelper.EmitAssembly(outcome.OutputCompilation));
    }

    [Fact]
    public void Host_GeneratesAggregator_ChainingReferencedModulesAndOwnServices()
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
        Assert.Contains("AddMyCompanyHostAllServices(", source);
        Assert.Contains(
            "global::Microsoft.Extensions.DependencyInjection.MyCompanyInfrastructureServiceCollectionExtensions" +
            ".AddMyCompanyInfrastructureServices(services);",
            source);
        Assert.Contains("services.AddMyCompanyHostServices();", source);
        Assert.Empty(host.CompilationErrors);
    }

    [Fact]
    public void Host_WithoutOwnServices_StillGeneratesAggregator()
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
        Assert.Contains("AddCompanyHostAllServices(", source);
        Assert.Contains(".AddCompanyLibServices(services);", source);
        Assert.DoesNotContain("services.AddCompanyHostServices();", source);
        Assert.Empty(host.CompilationErrors);
    }

    [Fact]
    public void Aggregator_ChainsModulesInDeterministicOrder()
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
        var alpha = source.IndexOf(".AddAlphaLibServices(services);", StringComparison.Ordinal);
        var beta = source.IndexOf(".AddBetaLibServices(services);", StringComparison.Ordinal);
        Assert.True(alpha >= 0 && beta >= 0 && alpha < beta,
            $"Expected AlphaLib before BetaLib. alpha={alpha}, beta={beta}");
        Assert.Empty(host.CompilationErrors);
    }

    [Fact]
    public void ProjectWithoutServicesOrModuleReferences_GeneratesNoAggregator()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            [SingletonService]
            public class Clock { }
            """);

        var source = outcome.GetSource(Hint);
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
}
