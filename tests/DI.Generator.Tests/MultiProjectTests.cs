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

    private static MetadataReference BuildLibraryReferenceWithoutMedi(string source, string assemblyName)
    {
        var outcome = GeneratorTestHelper.Run(
            source, assemblyName, baseReferences: GeneratorTestHelper.ReferencesWithoutMedi);
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
            ".CollectMyCompanyInfrastructureServices(registrations);",
            source);
        Assert.Contains("CollectMyCompanyHostServices(registrations);", source);
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
        Assert.Contains(".CollectCompanyLibServices(registrations);", source);
        Assert.DoesNotContain("CollectCompanyHostServices(registrations);", source);
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
        var alpha = source.IndexOf(".CollectAlphaLibServices(registrations);", StringComparison.Ordinal);
        var beta = source.IndexOf(".CollectBetaLibServices(registrations);", StringComparison.Ordinal);
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
            "registrations.Add((typeof(global::Domain.IOrderRepository), typeof(global::Infrastructure.SqlOrderRepository), " +
            "(int)global::DIGen.DiServiceScope.Scoped, null, false));",
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
            "registrations.Add((typeof(global::ThirdParty.IConnection), typeof(global::HostApp.RedisConnection), " +
            "(int)global::DIGen.DiServiceScope.Singleton, null, false));",
            host.GetSource(Hint));
        Assert.Empty(host.CompilationErrors);
    }

    [Fact]
    public void Aggregator_CallsMediFreeSharedModule_ExactlyOnce_EvenReferencedByTwoProjects()
    {
        // Diamond: Host -> A -> Shared, Host -> B -> Shared. Shared has no MEDI reference (only
        // [RequiredScope] + [Service<T>] self-registration). The aggregator must call Shared's
        // Collect method exactly once — never once per path that reaches it.
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

            namespace A;

            [SingletonService]
            public class AThing { }
            """,
            assemblyName: "A.Lib");

        var bRef = BuildLibraryReference("""
            using DIGen;

            namespace B;

            [SingletonService]
            public class BThing { }
            """,
            assemblyName: "B.Lib");

        var host = GeneratorTestHelper.Run(
            "namespace HostApp;",
            assemblyName: "Diamond.Host",
            extraReferences: [sharedRef, aRef, bRef]);

        var source = host.GetSource(Hint);
        var occurrences = System.Text.RegularExpressions.Regex.Matches(source, "CollectSharedLibServices").Count;
        Assert.Equal(1, occurrences);
        Assert.Empty(host.CompilationErrors);
    }
}
