using Xunit;

namespace NkChinh.DI.Generator.Tests;

public class ServiceRegistrationTests
{
    private const string Hint = "ServiceCollectionExtensions.g.cs";

    [Fact]
    public void EmbeddedAttributes_AreAlwaysGenerated()
    {
        var outcome = GeneratorTestHelper.Run("namespace Empty;");

        var attributes = outcome.GetSource("DIGenAttributes.g.cs");
        Assert.Contains("namespace DIGen", attributes);
        Assert.Contains("internal sealed class SingletonServiceAttribute", attributes);
        Assert.Contains("internal sealed class ScopedServiceAttribute", attributes);
        Assert.Contains("internal sealed class TransientServiceAttribute", attributes);
        Assert.Contains("internal sealed class InjectAttribute", attributes);
        Assert.Contains("class ServiceRegistrationModuleAttribute", attributes);
        Assert.Contains("#if !DIGEN_EXCLUDE_ATTRIBUTES", attributes);
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void SelfRegistration_RegistersConcreteType()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            [SingletonService]
            public class MemoryCache { }
            """);

        var source = outcome.GetSource(Hint);
        Assert.Contains(
            "registrations.Add((" +
            "typeof(global::Demo.MemoryCache), typeof(global::Demo.MemoryCache), " +
            "(int)global::DIGen.DiServiceScope.Singleton, null, false));",
            source);
        Assert.Contains("public static class TestAssemblyServiceCollectionExtensions", source);
        Assert.Contains("CollectTestAssemblyServices(", source);
        Assert.Contains("AddTestAssemblyServices(", source);
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void GenericAttribute_RegistersAsServiceType()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            public interface IOrderRepository { }

            [ScopedService<IOrderRepository>]
            public class OrderRepository : IOrderRepository { }
            """);

        var source = outcome.GetSource(Hint);
        Assert.Contains(
            "registrations.Add((" +
            "typeof(global::Demo.IOrderRepository), typeof(global::Demo.OrderRepository), " +
            "(int)global::DIGen.DiServiceScope.Scoped, null, false));",
            source);
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void GenericAttribute_WithBaseClass_RegistersAsBaseType()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            public class NotificationChannel { }

            [TransientService<NotificationChannel>]
            public class EmailChannel : NotificationChannel { }
            """);

        var source = outcome.GetSource(Hint);
        Assert.Contains(
            "registrations.Add((" +
            "typeof(global::Demo.NotificationChannel), typeof(global::Demo.EmailChannel), " +
            "(int)global::DIGen.DiServiceScope.Transient, null, false));",
            source);
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void KeyedRegistration_UsesKeyedOverloads()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            public interface IPaymentGateway { }

            [SingletonService<IPaymentGateway>("stripe")]
            public class StripeGateway : IPaymentGateway { }

            [TransientService("mem")]
            public class ScratchBuffer { }
            """);

        var source = outcome.GetSource(Hint);
        Assert.Contains(
            "registrations.Add((" +
            "typeof(global::Demo.IPaymentGateway), typeof(global::Demo.StripeGateway), " +
            "(int)global::DIGen.DiServiceScope.Singleton, \"stripe\", false));",
            source);
        Assert.Contains(
            "registrations.Add((" +
            "typeof(global::Demo.ScratchBuffer), typeof(global::Demo.ScratchBuffer), " +
            "(int)global::DIGen.DiServiceScope.Transient, \"mem\", false));",
            source);
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void HostedService_UsesAddHostedService()
    {
        var outcome = GeneratorTestHelper.Run("""
            using System.Threading;
            using System.Threading.Tasks;
            using DIGen;

            namespace Demo;

            [SingletonService]
            public class Worker : Microsoft.Extensions.Hosting.IHostedService
            {
                public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
                public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        var source = outcome.GetSource(Hint);
        Assert.Contains(
            "registrations.Add((" +
            "typeof(global::Microsoft.Extensions.Hosting.IHostedService), typeof(global::Demo.Worker), " +
            "(int)global::DIGen.DiServiceScope.Singleton, null, true));",
            source);
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void ExtensionMethodName_DerivesFromAssemblyName()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Infra;

            [SingletonService]
            public class Clock { }
            """,
            assemblyName: "MyCompany.Infrastructure");

        var source = outcome.GetSource(Hint);
        Assert.Contains("public static class MyCompanyInfrastructureServiceCollectionExtensions", source);
        Assert.Contains("AddMyCompanyInfrastructureServices(", source);
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void ModuleMarker_IsEmittedForOwnServices()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            [SingletonService]
            public class Clock { }
            """);

        var source = outcome.GetSource(Hint);
        Assert.Contains(
            "[assembly: global::DIGen.Generated.ServiceRegistrationModuleAttribute(" +
            "\"CollectTestAssemblyServices\", " +
            "\"Microsoft.Extensions.DependencyInjection.TestAssemblyServiceCollectionExtensions\")]",
            source);
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void NoAnnotatedServices_EmitsNoRegistrationFile()
    {
        var outcome = GeneratorTestHelper.Run("""
            namespace Demo;

            public class PlainClass { }
            """);

        Assert.False(outcome.HasSource(Hint));
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void Registrations_AreSortedDeterministically()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            [SingletonService]
            public class Zebra { }

            [SingletonService]
            public class Alpha { }
            """);

        var source = outcome.GetSource(Hint);
        var alpha = source.IndexOf("global::Demo.Alpha", StringComparison.Ordinal);
        var zebra = source.IndexOf("global::Demo.Zebra", StringComparison.Ordinal);
        Assert.True(alpha >= 0 && zebra >= 0 && alpha < zebra,
            $"Expected Alpha before Zebra. alpha={alpha}, zebra={zebra}");
    }
}
