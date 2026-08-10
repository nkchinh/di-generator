using Microsoft.CodeAnalysis;
using Xunit;

namespace NkChinh.DI.Generator.Tests;

public class NullableContextTests
{
    [Fact]
    public void NullableDisabled_GeneratedConstructorUsesObliviousMemberType()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            public interface IGreeter { }

            public partial class Consumer
            {
                [Inject] private readonly IGreeter _greeter;
            }
            """,
            nullableContextOptions: NullableContextOptions.Disable);

        var source = outcome.GetSource("Demo.Consumer.DependencyInjection.g.cs");
        Assert.Contains("global::Demo.IGreeter greeter", source);
        Assert.DoesNotContain("global::Demo.IGreeter?", source);
        Assert.DoesNotContain(
            outcome.OutputCompilation.GetDiagnostics(),
            static diagnostic => diagnostic.Id == "CS8632");
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void NullableEnabled_GeneratedConstructorPreservesNullableMemberType()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            public interface IGreeter { }

            public partial class Consumer
            {
                [Inject] private readonly IGreeter? _greeter;
            }
            """);

        var source = outcome.GetSource("Demo.Consumer.DependencyInjection.g.cs");
        Assert.Contains("global::Demo.IGreeter? greeter", source);
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void NullableDisabled_InitializerMarksInjectMemberOptional()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            public interface IDependency { }
            public interface IConsumer { }

            [SingletonService<IConsumer>]
            public partial class Consumer : IConsumer
            {
                [Inject] private readonly IDependency _dependency = null;

                public Consumer() => throw new global::System.NotSupportedException();
            }
            """,
            nullableContextOptions: NullableContextOptions.Disable);

        var source = outcome.GetSource("ServiceCollectionExtensions.g.cs");
        Assert.Contains("GetOptional<global::Demo.IDependency>(sp)", source);
        Assert.DoesNotContain(outcome.GeneratorDiagnostics, static d => d.Id == "DIGEN011");
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void NullableEnabled_NonNullableInitializerDoesNotMakeInjectMemberOptional()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            public interface IDependency { }
            public interface IConsumer { }
            public sealed class DefaultDependency : IDependency { }

            [SingletonService<IConsumer>]
            public partial class Consumer : IConsumer
            {
                [Inject] private readonly IDependency _dependency = new DefaultDependency();

                public Consumer() => throw new global::System.NotSupportedException();
            }
            """);

        var source = outcome.GetSource("ServiceCollectionExtensions.g.cs");
        Assert.Contains("GetRequired<global::Demo.IDependency>(sp)", source);
        Assert.Contains(outcome.GeneratorDiagnostics, static d => d.Id == "DIGEN011");
        Assert.Empty(outcome.CompilationErrors);
    }
}
