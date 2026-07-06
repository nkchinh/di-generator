# Contributing

Thanks for your interest in improving NkChinh.DI.Generator!

## Development setup

- .NET SDK 10.0.x (tests also run on the .NET 8 runtime; install both for full coverage)
- Any editor; `IsRoslynComponent=true` enables the Roslyn component debugger in Visual Studio/Rider

```bash
dotnet pack src/DI.Generator -c Release -o artifacts   # samples restore the package from ./artifacts
dotnet build DI.Generator.slnx -c Release
dotnet test DI.Generator.slnx -c Release
dotnet run --project samples/Sample.Host
```

After changing the generator, repack and clear the cached `nkchinh.di.generator` package
(`dotnet nuget locals global-packages --clear`) so the samples pick up the new build.

## Ground rules

1. **Test-first.** Every behavior change starts with a failing test
   (`tests/DI.Generator.Tests`), every bug fix with a reproduction test.
2. **Snapshots are reviewed, not regenerated blindly.** If a `*.received.*` file appears,
   inspect the diff before promoting it to `*.verified.*`.
3. **Keep the pipeline incremental.** Never flow `ISymbol`/`SyntaxNode` through the pipeline;
   models must be value-equatable (see `Models.cs`). The cacheability test guards this.
4. **Diagnostics over broken code.** If the generator can't emit something valid, report a
   `DIGEN###` diagnostic. New diagnostics need an entry in `AnalyzerReleases.Unshipped.md`,
   `docs/diagnostics.md`, and both a "fires" and a "does not misfire" test.
5. **Pure generator.** No runtime dependencies may be added to the package. The nupkg must
   contain only the analyzer DLL (checked in CI).

## Pull requests

- Reference the spec section (`docs/SPEC.md`) your change implements or amends; update the
  spec when the design changes.
- Update `CHANGELOG.md` under **Unreleased**.
- CI must pass on both net8.0 and net10.0 test runs.
