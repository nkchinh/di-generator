using System.Runtime.CompilerServices;

namespace NkChinh.DI.Generator.Tests;

public static class ModuleInit
{
    [ModuleInitializer]
    public static void Init() => VerifySourceGenerators.Initialize();
}
