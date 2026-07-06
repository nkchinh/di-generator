using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace NkChinh.DI.Generator.Tests;

/// <summary>
/// Golden-file snapshots (Verify.SourceGenerators) locking the full text of generated output.
/// Review any .received.* file before promoting it to .verified.*.
/// </summary>
public class SnapshotTests
{
    private static Task VerifyDriver(string source, string assemblyName = "Snapshot.Assembly")
    {
        var compilation = GeneratorTestHelper.CreateCompilation([source], assemblyName);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new DependencyInjectionGenerator().AsSourceGenerator()],
            parseOptions: GeneratorTestHelper.ParseOptions);
        driver = driver.RunGenerators(compilation);

        var settings = new VerifySettings();
        settings.UseDirectory("Snapshots");
        return Verifier.Verify(driver, settings);
    }

    [Fact]
    public Task EmbeddedAttributes()
        => VerifyDriver("namespace Empty;");

    [Fact]
    public Task ServiceRegistrations()
        => VerifyDriver("""
            using DIGen;

            namespace Demo;

            public interface IOrderRepository { }
            public interface IPaymentGateway { }

            [ScopedService<IOrderRepository>]
            public class OrderRepository : IOrderRepository { }

            [SingletonService<IPaymentGateway>("stripe")]
            public class StripeGateway : IPaymentGateway { }

            [TransientService]
            public class ScratchBuffer { }
            """);

    [Fact]
    public Task InjectConstructor()
        => VerifyDriver("""
            using DIGen;

            namespace Demo;

            public interface IOrderRepository { }
            public interface IPaymentGateway { }

            public partial class OrderService
            {
                [Inject] private readonly IOrderRepository _repository;
                [Inject] private readonly IPaymentGateway _gateway;
            }
            """);
}
