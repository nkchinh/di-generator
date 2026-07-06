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
