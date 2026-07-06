; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DIGEN001 | NkChinh.DI.Generator | Error | Service type not implemented or inherited
DIGEN002 | NkChinh.DI.Generator | Error | [Inject] containing type must be partial
DIGEN003 | NkChinh.DI.Generator | Error | [Inject] member must not be static or const
DIGEN004 | NkChinh.DI.Generator | Error | [Inject] property not assignable from constructor
DIGEN005 | NkChinh.DI.Generator | Warning | Lifetime attribute on abstract class is ignored
DIGEN006 | NkChinh.DI.Generator | Error | Multiple lifetime attributes on one class
DIGEN007 | NkChinh.DI.Generator | Error | [Inject] only supported inside classes
DIGEN008 | NkChinh.DI.Generator | Error | [Service<T>] used but T has no locked required scope
DIGEN009 | NkChinh.DI.Generator | Error | Lifetime attribute disagrees with T's locked required scope
DIGEN010 | NkChinh.DI.Generator | Error | Conflicting [assembly: RequiredExternalScope] declarations for the same type
