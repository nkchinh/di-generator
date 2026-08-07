using Microsoft.CodeAnalysis;
using Xunit;

namespace NkChinh.DI.Generator.Tests;

public class RequiredScopeTests
{
    private const string Hint = "ServiceCollectionExtensions.g.cs";

    [Fact]
    public void ServiceAttribute_ResolvesLifetime_FromRequiredScopeOnInterface()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            [RequiredScope(DiServiceScope.Scoped)]
            public interface IOrderRepository { }

            [Service<IOrderRepository>]
            public class OrderRepository : IOrderRepository { }
            """);

        var source = outcome.GetSource(Hint);
        Assert.Contains(
            "registrations.Add((typeof(global::Demo.IOrderRepository), typeof(global::Demo.OrderRepository), " +
            "(int)global::DIGen.DiServiceScope.Scoped, null, false, null));",
            source);
        Assert.Empty(outcome.GeneratorDiagnostics);
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void ServiceAttribute_SupportsKeyedRegistration()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            [RequiredScope(DiServiceScope.Singleton)]
            public interface IPaymentGateway { }

            [Service<IPaymentGateway>("stripe")]
            public class StripeGateway : IPaymentGateway { }
            """);

        var source = outcome.GetSource(Hint);
        Assert.Contains(
            "registrations.Add((typeof(global::Demo.IPaymentGateway), typeof(global::Demo.StripeGateway), " +
            "(int)global::DIGen.DiServiceScope.Singleton, \"stripe\", false, null));",
            source);
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void ServiceAttribute_ResolvesLifetime_FromRequiredExternalScope()
    {
        var outcome = GeneratorTestHelper.Run("""
            using System;
            using DIGen;

            [assembly: RequiredExternalScope(typeof(Demo.IConnection), DiServiceScope.Singleton)]

            namespace Demo;

            public interface IConnection { }

            [Service<IConnection>]
            public class RedisConnection : IConnection { }
            """);

        var source = outcome.GetSource(Hint);
        Assert.Contains(
            "registrations.Add((typeof(global::Demo.IConnection), typeof(global::Demo.RedisConnection), " +
            "(int)global::DIGen.DiServiceScope.Singleton, null, false, null));",
            source);
        Assert.Empty(outcome.GeneratorDiagnostics);
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void DIGEN008_Fires_WhenServiceAttributeHasNoLockedScope()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            public interface IOrderRepository { }

            [Service<IOrderRepository>]
            public class OrderRepository : IOrderRepository { }
            """);

        var diagnostic = Assert.Single(outcome.GeneratorDiagnostics, static d => d.Id == "DIGEN008");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("IOrderRepository", diagnostic.GetMessage());
        Assert.False(outcome.HasSource(Hint));
    }

    [Fact]
    public void DIGEN009_Fires_WhenExplicitLifetimeDisagreesWithLockedScope()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            [RequiredScope(DiServiceScope.Scoped)]
            public interface IOrderRepository { }

            [SingletonService<IOrderRepository>]
            public class OrderRepository : IOrderRepository { }
            """);

        var diagnostic = Assert.Single(outcome.GeneratorDiagnostics, static d => d.Id == "DIGEN009");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.False(outcome.HasSource(Hint));
    }

    [Fact]
    public void DIGEN009_DoesNotFire_WhenExplicitLifetimeMatchesLockedScope()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            [RequiredScope(DiServiceScope.Scoped)]
            public interface IOrderRepository { }

            [ScopedService<IOrderRepository>]
            public class OrderRepository : IOrderRepository { }
            """);

        Assert.DoesNotContain(outcome.GeneratorDiagnostics, static d => d.Id == "DIGEN009");
        Assert.Contains(
            "registrations.Add((typeof(global::Demo.IOrderRepository), typeof(global::Demo.OrderRepository), " +
            "(int)global::DIGen.DiServiceScope.Scoped, null, false, null));",
            outcome.GetSource(Hint));
    }

    [Fact]
    public void RequiredScope_TakesPrecedenceOverConflictingRequiredExternalScope()
    {
        var outcome = GeneratorTestHelper.Run("""
            using System;
            using DIGen;

            [assembly: RequiredExternalScope(typeof(Demo.IOrderRepository), DiServiceScope.Singleton)]

            namespace Demo;

            [RequiredScope(DiServiceScope.Scoped)]
            public interface IOrderRepository { }

            [Service<IOrderRepository>]
            public class OrderRepository : IOrderRepository { }
            """);

        Assert.Empty(outcome.GeneratorDiagnostics);
        Assert.Contains(
            "registrations.Add((typeof(global::Demo.IOrderRepository), typeof(global::Demo.OrderRepository), " +
            "(int)global::DIGen.DiServiceScope.Scoped, null, false, null));",
            outcome.GetSource(Hint));
    }

    [Fact]
    public void DIGEN010_Fires_WhenRequiredExternalScopeConflicts()
    {
        var outcome = GeneratorTestHelper.Run("""
            using System;
            using DIGen;

            [assembly: RequiredExternalScope(typeof(Demo.IConnection), DiServiceScope.Singleton)]
            [assembly: RequiredExternalScope(typeof(Demo.IConnection), DiServiceScope.Scoped)]

            namespace Demo;

            public interface IConnection { }

            [Service<IConnection>]
            public class RedisConnection : IConnection { }
            """);

        var diagnostic = Assert.Single(outcome.GeneratorDiagnostics, static d => d.Id == "DIGEN010");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("IConnection", diagnostic.GetMessage());
    }

    [Fact]
    public void UnlockedInterface_ExplicitLifetimeAttribute_StillWorksAsBefore()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            public interface IOrderRepository { }

            [ScopedService<IOrderRepository>]
            public class OrderRepository : IOrderRepository { }
            """);

        Assert.Empty(outcome.GeneratorDiagnostics);
        Assert.Contains(
            "registrations.Add((typeof(global::Demo.IOrderRepository), typeof(global::Demo.OrderRepository), " +
            "(int)global::DIGen.DiServiceScope.Scoped, null, false, null));",
            outcome.GetSource(Hint));
    }

    [Fact]
    public void EmbeddedAttributes_IncludeRequiredScopeTypes()
    {
        var outcome = GeneratorTestHelper.Run("namespace Empty;");

        var attributes = outcome.GetSource("DIGenAttributes.g.cs");
        Assert.Contains("internal enum DiServiceScope", attributes);
        Assert.Contains("internal sealed class RequiredScopeAttribute", attributes);
        Assert.Contains("internal sealed class RequiredExternalScopeAttribute", attributes);
        Assert.Contains("internal sealed class ServiceAttribute<TService>", attributes);
    }

    [Fact]
    public void LifetimeExtensions_AreEmitted_WhenProjectReferencesServiceLifetime()
    {
        // GeneratorTestHelper always references Microsoft.Extensions.DependencyInjection.Abstractions.
        var outcome = GeneratorTestHelper.Run("namespace Empty;");

        var extensions = outcome.GetSource("DiServiceScopeExtensions.g.cs");
        Assert.Contains("ToServiceLifetime", extensions);
        Assert.Contains("Microsoft.Extensions.DependencyInjection.ServiceLifetime", extensions);
    }

    [Fact]
    public void LifetimeExtensions_AreNotEmitted_WhenProjectHasNoMediReference()
    {
        var outcome = GeneratorTestHelper.Run(
            "namespace Empty;",
            baseReferences: GeneratorTestHelper.ReferencesWithoutMedi);

        Assert.False(outcome.HasSource("DiServiceScopeExtensions.g.cs"));
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void PureDomainProject_WithRequiredScopeAndServiceAttribute_CompilesWithNoMediReference()
    {
        // The core promise of Required Scope Validation: a Domain/Application project that only
        // declares interfaces, locks their scope, and self-registers via [Service<T>] needs zero
        // reference to Microsoft.Extensions.DependencyInjection.
        var outcome = GeneratorTestHelper.Run(
            """
            using DIGen;

            namespace Domain;

            [RequiredScope(DiServiceScope.Scoped)]
            public interface IOrderRepository { }

            [Service<IOrderRepository>]
            public class SqlOrderRepository : IOrderRepository { }
            """,
            baseReferences: GeneratorTestHelper.ReferencesWithoutMedi);

        Assert.Empty(outcome.GeneratorDiagnostics);
        Assert.Empty(outcome.CompilationErrors);

        var source = outcome.GetSource(Hint);
        Assert.Contains("CollectTestAssemblyServices", source);
        Assert.Contains(
            "registrations.Add((typeof(global::Domain.IOrderRepository), typeof(global::Domain.SqlOrderRepository), " +
            "(int)global::DIGen.DiServiceScope.Scoped, null, false, null));",
            source);
        // No MEDI reference => no IServiceCollection-based convenience methods, and no aggregator.
        Assert.DoesNotContain("AddTestAssemblyServices", source);
        Assert.DoesNotContain("AllServices", source);
        Assert.DoesNotContain("IServiceCollection", source);
    }
}
