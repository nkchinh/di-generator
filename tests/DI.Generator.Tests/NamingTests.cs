using Xunit;

namespace NkChinh.DI.Generator.Tests;

public class NamingTests
{
    [Theory]
    [InlineData("MyCompany.Infrastructure", "MyCompanyInfrastructure")]
    [InlineData("my-lib", "MyLib")]
    [InlineData("sample_host", "SampleHost")]
    [InlineData("Simple", "Simple")]
    [InlineData("123abc", "_123abc")]
    [InlineData("weird!!name", "WeirdName")]
    [InlineData("", "Assembly")]
    public void SanitizeAssemblyIdentifier_ProducesValidPascalCaseIdentifier(string assemblyName, string expected)
        => Assert.Equal(expected, NameHelper.SanitizeAssemblyIdentifier(assemblyName));

    [Theory]
    [InlineData("IOrderRepository", "_repository", "orderRepository")]
    [InlineData("Cache", "_cache", "cache")]
    [InlineData("IClock", "Clock", "clock")]
    [InlineData("String", "_connectionString", "connectionString")] // "string" is a keyword → member fallback
    [InlineData("", "_items", "items")]                             // arrays etc. have no usable type name
    [InlineData("IO", "_io", "io")]                                 // all-caps short names are fully lowered
    [InlineData("String", "_class", "@class")]                      // keyword member fallback gets escaped
    public void DeriveParameterName_FollowsTypeNameThenMemberNameRules(
        string typeShortName, string memberName, string expected)
        => Assert.Equal(expected, NameHelper.DeriveParameterName(typeShortName, memberName));
}
