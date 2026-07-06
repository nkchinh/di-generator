using Microsoft.CodeAnalysis;
using Xunit;

namespace NkChinh.DI.Generator.Tests;

public class DiagnosticTests
{
    private static Diagnostic AssertSingle(GeneratorRunOutcome outcome, string id, DiagnosticSeverity severity)
    {
        var diagnostic = Assert.Single(outcome.GeneratorDiagnostics, d => d.Id == id);
        Assert.Equal(severity, diagnostic.Severity);
        return diagnostic;
    }

    [Fact]
    public void DIGEN001_Fires_WhenClassDoesNotImplementServiceType()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            public interface IOrderRepository { }

            [SingletonService<IOrderRepository>]
            public class Unrelated { }
            """);

        var diagnostic = AssertSingle(outcome, "DIGEN001", DiagnosticSeverity.Error);
        Assert.Contains("Unrelated", diagnostic.GetMessage());
        Assert.Contains("IOrderRepository", diagnostic.GetMessage());
        Assert.False(outcome.HasSource("ServiceCollectionExtensions.g.cs"));
    }

    [Fact]
    public void DIGEN001_DoesNotFire_ForInheritedInterface()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            public interface IBase { }
            public interface IDerived : IBase { }

            [SingletonService<IBase>]
            public class Impl : IDerived { }
            """);

        Assert.DoesNotContain(outcome.GeneratorDiagnostics, static d => d.Id == "DIGEN001");
        Assert.Contains("services.AddSingleton<global::Demo.IBase, global::Demo.Impl>();",
            outcome.GetSource("ServiceCollectionExtensions.g.cs"));
    }

    [Fact]
    public void DIGEN002_Fires_WhenInjectClassIsNotPartial()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            public interface IClock { }

            public class NotPartial
            {
                [Inject] private readonly IClock _clock;
            }
            """);

        AssertSingle(outcome, "DIGEN002", DiagnosticSeverity.Error);
        Assert.False(outcome.HasSource(".DependencyInjection.g.cs"));
    }

    [Fact]
    public void DIGEN002_Fires_WhenContainingOuterTypeIsNotPartial()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            public interface IClock { }

            public class Outer
            {
                public partial class Inner
                {
                    [Inject] private readonly IClock _clock;
                }
            }
            """);

        var diagnostic = Assert.Single(outcome.GeneratorDiagnostics, static d => d.Id == "DIGEN002");
        Assert.Contains("Outer", diagnostic.GetMessage());
    }

    [Fact]
    public void DIGEN003_Fires_ForStaticInjectMember()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            public interface IClock { }

            public partial class Holder
            {
                [Inject] private static IClock? _clock;
            }
            """);

        AssertSingle(outcome, "DIGEN003", DiagnosticSeverity.Error);
        Assert.False(outcome.HasSource(".DependencyInjection.g.cs"));
    }

    [Fact]
    public void DIGEN004_Fires_ForNonAssignableProperty()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            public interface IClock { }

            public partial class Holder
            {
                [Inject] public IClock Clock => null!;
            }
            """);

        AssertSingle(outcome, "DIGEN004", DiagnosticSeverity.Error);
    }

    [Fact]
    public void DIGEN005_Warns_AndSkipsAbstractClass()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            [SingletonService]
            public abstract class BaseHandler { }
            """);

        AssertSingle(outcome, "DIGEN005", DiagnosticSeverity.Warning);
        Assert.False(outcome.HasSource("ServiceCollectionExtensions.g.cs"));
    }

    [Fact]
    public void DIGEN006_Fires_WhenMultipleLifetimeAttributesArePresent()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            [SingletonService]
            [ScopedService]
            public class Confused { }
            """);

        var diagnostic = Assert.Single(
            outcome.GeneratorDiagnostics.Where(static d => d.Id == "DIGEN006").Take(1));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.False(outcome.HasSource("ServiceCollectionExtensions.g.cs"));
    }

    [Fact]
    public void DIGEN007_Fires_ForInjectInsideStruct()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            public interface IClock { }

            public partial struct Widget
            {
                [Inject] private IClock? _clock;
            }
            """);

        AssertSingle(outcome, "DIGEN007", DiagnosticSeverity.Error);
        Assert.False(outcome.HasSource(".DependencyInjection.g.cs"));
    }
}
