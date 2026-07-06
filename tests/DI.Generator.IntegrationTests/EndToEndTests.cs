using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace NkChinh.DI.Generator.IntegrationTests;

public class EndToEndTests
{
    private static ServiceProvider BuildProvider(Assembly assembly, string extensionsTypeName, string methodName)
    {
        var services = new ServiceCollection();
        var extensionsType = assembly.GetType(extensionsTypeName)
            ?? throw new InvalidOperationException($"Type '{extensionsTypeName}' not found in generated assembly.");
        var method = extensionsType.GetMethod(methodName)
            ?? throw new InvalidOperationException($"Method '{methodName}' not found on '{extensionsTypeName}'.");
        method.Invoke(null, [services]);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void BasicRegistrations_ResolveWithCorrectLifetimes()
    {
        var image = RuntimeHelper.CompileWithGenerator("""
            using DIGen;

            namespace Demo;

            public interface IGreeter { string Greet(); }

            [SingletonService<IGreeter>]
            public class Greeter : IGreeter
            {
                public string Greet() => "hello";
            }

            [TransientService]
            public class Buffer { }
            """,
            assemblyName: "BasicApp");

        var assembly = RuntimeHelper.Load(image);
        using var provider = BuildProvider(
            assembly,
            "Microsoft.Extensions.DependencyInjection.BasicAppServiceCollectionExtensions",
            "AddBasicAppServices");

        var greeterType = assembly.GetType("Demo.IGreeter")!;
        var first = provider.GetRequiredService(greeterType);
        var second = provider.GetRequiredService(greeterType);
        Assert.Same(first, second);
        Assert.Equal("hello", greeterType.GetMethod("Greet")!.Invoke(first, null));

        var bufferType = assembly.GetType("Demo.Buffer")!;
        Assert.NotSame(provider.GetRequiredService(bufferType), provider.GetRequiredService(bufferType));
    }

    [Fact]
    public void KeyedRegistrations_ResolveByKey()
    {
        var image = RuntimeHelper.CompileWithGenerator("""
            using DIGen;

            namespace Demo;

            public interface IGateway { string Name { get; } }

            [SingletonService<IGateway>("stripe")]
            public class StripeGateway : IGateway { public string Name => "stripe"; }

            [SingletonService<IGateway>("paypal")]
            public class PayPalGateway : IGateway { public string Name => "paypal"; }
            """,
            assemblyName: "KeyedApp");

        var assembly = RuntimeHelper.Load(image);
        using var provider = BuildProvider(
            assembly,
            "Microsoft.Extensions.DependencyInjection.KeyedAppServiceCollectionExtensions",
            "AddKeyedAppServices");

        var gatewayType = assembly.GetType("Demo.IGateway")!;
        var stripe = provider.GetRequiredKeyedService(gatewayType, "stripe");
        var paypal = provider.GetRequiredKeyedService(gatewayType, "paypal");
        Assert.Equal("stripe", gatewayType.GetProperty("Name")!.GetValue(stripe));
        Assert.Equal("paypal", gatewayType.GetProperty("Name")!.GetValue(paypal));
    }

    [Fact]
    public void HostedService_IsRegisteredAsIHostedService()
    {
        var image = RuntimeHelper.CompileWithGenerator("""
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.Hosting;
            using DIGen;

            namespace Demo;

            [SingletonService]
            public class Worker : IHostedService
            {
                public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
                public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """,
            assemblyName: "HostedApp");

        var assembly = RuntimeHelper.Load(image);
        using var provider = BuildProvider(
            assembly,
            "Microsoft.Extensions.DependencyInjection.HostedAppServiceCollectionExtensions",
            "AddHostedAppServices");

        var hosted = provider.GetServices<IHostedService>().ToList();
        Assert.Single(hosted);
        Assert.Equal("Worker", hosted[0].GetType().Name);
    }

    [Fact]
    public void InjectConstructor_ReceivesDependenciesAtRuntime()
    {
        var image = RuntimeHelper.CompileWithGenerator("""
            using DIGen;

            namespace Demo;

            public interface IGreeter { string Greet(); }

            [SingletonService<IGreeter>]
            public class Greeter : IGreeter
            {
                public string Greet() => "hello from greeter";
            }

            public interface IApp { string Run(); }

            [SingletonService<IApp>]
            public partial class App : IApp
            {
                [Inject] private readonly IGreeter _greeter;

                public string Run() => _greeter.Greet();
            }
            """,
            assemblyName: "InjectApp");

        var assembly = RuntimeHelper.Load(image);
        using var provider = BuildProvider(
            assembly,
            "Microsoft.Extensions.DependencyInjection.InjectAppServiceCollectionExtensions",
            "AddInjectAppServices");

        var appType = assembly.GetType("Demo.IApp")!;
        var app = provider.GetRequiredService(appType);
        Assert.Equal("hello from greeter", appType.GetMethod("Run")!.Invoke(app, null));
    }

    [Fact]
    public void ActivatorUtilities_PicksGeneratedConstructor_OverCompetingUserConstructor()
    {
        var image = RuntimeHelper.CompileWithGenerator("""
            using DIGen;

            namespace Demo;

            public interface IGreeter { string Greet(); }

            [SingletonService<IGreeter>]
            public class Greeter : IGreeter
            {
                public string Greet() => "injected";
            }

            public partial class Consumer
            {
                [Inject] private readonly IGreeter _greeter;

                public Consumer() { _greeter = null!; }

                public string Describe() => _greeter is null ? "default ctor" : _greeter.Greet();
            }
            """,
            assemblyName: "CtorPickApp");

        var assembly = RuntimeHelper.Load(image);
        using var provider = BuildProvider(
            assembly,
            "Microsoft.Extensions.DependencyInjection.CtorPickAppServiceCollectionExtensions",
            "AddCtorPickAppServices");

        var consumerType = assembly.GetType("Demo.Consumer")!;
        var generatedCtor = consumerType.GetConstructors()
            .Single(static c => c.GetParameters().Length == 1);
        Assert.Contains(generatedCtor.GetCustomAttributes(), static a =>
            a.GetType().Name == nameof(ActivatorUtilitiesConstructorAttribute));

        // [ActivatorUtilitiesConstructor] must win over the parameterless constructor.
        var consumer = ActivatorUtilities.CreateInstance(provider, consumerType);
        Assert.Equal("injected", consumerType.GetMethod("Describe")!.Invoke(consumer, null));
    }

    [Fact]
    public void MultiProject_AggregatorChainsLibraryAndHostServices()
    {
        var libImage = RuntimeHelper.CompileWithGenerator("""
            using DIGen;

            namespace Infra;

            public interface IRepository { string Source { get; } }

            [ScopedService<IRepository>]
            public class InMemoryRepository : IRepository
            {
                public string Source => "library";
            }
            """,
            assemblyName: "IntegrationLib");

        var hostImage = RuntimeHelper.CompileWithGenerator("""
            using Infra;
            using DIGen;

            namespace HostApp;

            public interface IUseCase { string Execute(); }

            [TransientService<IUseCase>]
            public partial class UseCase : IUseCase
            {
                [Inject] private readonly IRepository _repository;

                public string Execute() => "handled by " + _repository.Source;
            }
            """,
            assemblyName: "IntegrationHost",
            referencedImages: libImage);

        var hostAssembly = RuntimeHelper.Load(hostImage, ("IntegrationLib", libImage));
        using var provider = BuildProvider(
            hostAssembly,
            "Microsoft.Extensions.DependencyInjection.IntegrationHostServiceCollectionExtensions",
            "AddIntegrationHostAllServices");

        var useCaseType = hostAssembly.GetType("HostApp.IUseCase")!;
        using var scope = provider.CreateScope();
        var useCase = scope.ServiceProvider.GetRequiredService(useCaseType);
        Assert.Equal("handled by library", useCaseType.GetMethod("Execute")!.Invoke(useCase, null));
    }

    [Fact]
    public void ServiceAttribute_ResolvesLifetimeAcrossProjects_AndRegistersWithIt()
    {
        var domainImage = RuntimeHelper.CompileWithGenerator("""
            using DIGen;

            namespace Domain;

            [RequiredScope(DiServiceScope.Scoped)]
            public interface IOrderRepository { }
            """,
            assemblyName: "ScopeDomain");

        var infrastructureImage = RuntimeHelper.CompileWithGenerator("""
            using System;
            using Domain;
            using DIGen;

            [assembly: RequiredExternalScope(typeof(ThirdParty.IConnection), DiServiceScope.Singleton)]

            namespace Infra
            {
                [Service<IOrderRepository>]
                public class SqlOrderRepository : IOrderRepository { }
            }

            namespace ThirdParty
            {
                public interface IConnection { }
            }

            namespace Infra2
            {
                [DIGen.Service<ThirdParty.IConnection>]
                public class RedisConnection : ThirdParty.IConnection { }
            }
            """,
            assemblyName: "ScopeInfrastructure",
            referencedImages: domainImage);

        var assembly = RuntimeHelper.Load(infrastructureImage, ("ScopeDomain", domainImage));
        using var provider = BuildProvider(
            assembly,
            "Microsoft.Extensions.DependencyInjection.ScopeInfrastructureServiceCollectionExtensions",
            "AddScopeInfrastructureServices");

        // IOrderRepository lives in the Domain assembly; resolve it through the impl type's
        // interfaces so it shares type identity with whatever the generated code registered.
        var repositoryType = assembly.GetType("Infra.SqlOrderRepository")!
            .GetInterfaces().Single(static t => t.Name == "IOrderRepository");
        using (var scopeA = provider.CreateScope())
        {
            var first = scopeA.ServiceProvider.GetRequiredService(repositoryType);
            var second = scopeA.ServiceProvider.GetRequiredService(repositoryType);
            Assert.Same(first, second); // same scope → same instance (Scoped)

            using var scopeB = provider.CreateScope();
            var third = scopeB.ServiceProvider.GetRequiredService(repositoryType);
            Assert.NotSame(first, third); // different scope → different instance (Scoped, not Singleton)
        }

        var connectionType = assembly.GetType("ThirdParty.IConnection")!;
        Assert.Same( // Singleton, per the RequiredExternalScope lock
            provider.GetRequiredService(connectionType),
            provider.GetRequiredService(connectionType));
    }
}
