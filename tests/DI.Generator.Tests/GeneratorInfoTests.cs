using System.Reflection;
using Xunit;

namespace NkChinh.DI.Generator.Tests;

public class GeneratorInfoTests
{
    [Fact]
    public void GeneratedCodeVersionMatchesGeneratorAssemblyVersion()
    {
        var expectedVersion = typeof(DependencyInjectionGenerator).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion
            .Split('+')[0];

        Assert.NotEqual("0.0.0", expectedVersion);
        Assert.Equal(GeneratorInfo.Version, expectedVersion);
        Assert.Contains(
            $"GeneratedCode(\"{GeneratorInfo.Name}\", \"{expectedVersion}\")",
            EmbeddedSources.Attributes);
    }
}
