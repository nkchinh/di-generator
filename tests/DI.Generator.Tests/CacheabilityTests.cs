using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace NkChinh.DI.Generator.Tests;

public class CacheabilityTests
{
    private const string Source = """
        using DIGen;

        namespace Demo;

        public interface IRepo { }

        [ScopedService<IRepo>]
        public partial class Repo : IRepo
        {
            [Inject] private readonly IRepo _inner;
        }
        """;

    [Fact]
    public void SecondRun_WithUnrelatedChange_ReusesCachedPipelineOutputs()
    {
        var compilation = GeneratorTestHelper.CreateCompilation([Source]);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new DependencyInjectionGenerator().AsSourceGenerator()],
            parseOptions: GeneratorTestHelper.ParseOptions,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);
        var firstTexts = GeneratedTexts(driver);

        var updated = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("// unrelated change", GeneratorTestHelper.ParseOptions, path: "Unrelated.cs"));
        driver = driver.RunGenerators(updated);

        var result = driver.GetRunResult().Results.Single();
        foreach (var stepName in new[] { "Services", "Injects" })
        {
            Assert.True(result.TrackedSteps.ContainsKey(stepName), $"Missing tracked step '{stepName}'");
            var reasons = result.TrackedSteps[stepName]
                .SelectMany(static s => s.Outputs)
                .Select(static o => o.Reason);
            Assert.All(reasons, static r =>
                Assert.True(r is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                    $"Pipeline output was recomputed: {r}"));
        }

        Assert.Equal(firstTexts, GeneratedTexts(driver));
    }

    private static Dictionary<string, string> GeneratedTexts(GeneratorDriver driver)
        => driver.GetRunResult().Results.Single().GeneratedSources
            .ToDictionary(static s => s.HintName, static s => s.SourceText.ToString());
}
