using System.Text.RegularExpressions;
using Xunit;

namespace NkChinh.DI.Generator.Tests;

public class InjectConstructorTests
{
    private const string Hint = ".DependencyInjection.g.cs";

    [Fact]
    public void InjectMembers_AreGroupedIntoSingleConstructor()
    {
        var outcome = GeneratorTestHelper.Run("""
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

        var source = outcome.GetSource(Hint);

        Assert.Contains("[global::Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]", source);
        Assert.Contains(
            "public OrderService(global::Demo.IOrderRepository orderRepository, global::Demo.IPaymentGateway paymentGateway)",
            source);
        Assert.Contains("this._repository = orderRepository;", source);
        Assert.Contains("this._gateway = paymentGateway;", source);
        Assert.Single(Regex.Matches(source, @"public OrderService\("));
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void ParameterName_IsCamelCasedFromTypeName_StrippingInterfacePrefix()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            public interface IOrderRepository { }

            public partial class OrderService
            {
                [Inject] private readonly IOrderRepository _repository;
            }
            """);

        var source = outcome.GetSource(Hint);
        Assert.Contains("public OrderService(global::Demo.IOrderRepository orderRepository)", source);
        Assert.Contains("this._repository = orderRepository;", source);
    }

    [Fact]
    public void PropertyInjection_AssignsProperty()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            public interface IClock { }

            public partial class Scheduler
            {
                [Inject] public IClock Clock { get; }
            }
            """);

        var source = outcome.GetSource(Hint);
        Assert.Contains("public Scheduler(global::Demo.IClock clock)", source);
        Assert.Contains("this.Clock = clock;", source);
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void DuplicateParameterNames_FallBackToMemberNames()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            public interface IChannel { }

            public partial class Router
            {
                [Inject] private readonly IChannel _primary;
                [Inject] private readonly IChannel _secondary;
            }
            """);

        var source = outcome.GetSource(Hint);
        Assert.Contains("public Router(global::Demo.IChannel primary, global::Demo.IChannel secondary)", source);
        Assert.Contains("this._primary = primary;", source);
        Assert.Contains("this._secondary = secondary;", source);
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void KeywordTypeName_FallsBackToMemberName()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            public partial class Config
            {
                [Inject] private readonly string _connectionString;
            }
            """);

        var source = outcome.GetSource(Hint);
        Assert.Contains("public Config(string connectionString)", source);
        Assert.Contains("this._connectionString = connectionString;", source);
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void GenericClass_GetsGenericPartialDeclaration()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            public interface IValidator<T> { }

            public partial class Pipeline<T>
            {
                [Inject] private readonly IValidator<T> _validator;
            }
            """);

        var source = outcome.GetSource(Hint);
        Assert.Contains("partial class Pipeline<T>", source);
        Assert.Contains("public Pipeline(global::Demo.IValidator<T> validator)", source);
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void NestedClass_GetsNestedPartialChain()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            namespace Demo;

            public interface IClock { }

            public partial class Outer
            {
                public partial class Inner
                {
                    [Inject] private readonly IClock _clock;
                }
            }
            """);

        var source = outcome.GetSource(Hint);
        Assert.Contains("partial class Outer", source);
        Assert.Contains("partial class Inner", source);
        Assert.Contains("public Inner(global::Demo.IClock clock)", source);
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void PartialClassAcrossFiles_StillGetsSingleConstructor()
    {
        var outcome = GeneratorTestHelper.Run(new[]
        {
            """
            using DIGen;

            namespace Demo;

            public interface IClock { }

            public partial class Split
            {
                [Inject] private readonly IClock _clock;
            }
            """,
            """
            using DIGen;

            namespace Demo;

            public interface ILog { }

            public partial class Split
            {
                [Inject] private readonly ILog _log;
            }
            """,
        });

        var source = outcome.GetSource(Hint);
        Assert.Single(Regex.Matches(source, @"public Split\("));
        Assert.Contains("this._clock = clock;", source);
        Assert.Contains("this._log = log;", source);
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void ClassInGlobalNamespace_IsSupported()
    {
        var outcome = GeneratorTestHelper.Run("""
            using DIGen;

            public interface IClock { }

            public partial class GlobalHolder
            {
                [Inject] private readonly IClock _clock;
            }
            """);

        var source = outcome.GetSource(Hint);
        Assert.Contains("public GlobalHolder(global::IClock clock)", source);
        Assert.Empty(outcome.CompilationErrors);
    }
}
