# Spec: NkChinh.DI.Generator

A production-ready, **pure** Roslyn incremental source generator that provides attribute-driven
dependency-injection registration and constructor injection for `Microsoft.Extensions.DependencyInjection`,
shipped as a NuGet analyzer package with zero runtime dependencies.

## Objective

Evolve a legacy project-specific `ServiceCollectionGenerator.cs` into a general-purpose NuGet package:

* Users annotate classes with `[SingletonService]`, `[ScopedService<T>]`, `[TransientService]`, … and get a
  per-assembly `IServiceCollection` extension method that registers everything.
* Users annotate fields/properties with `[Inject]` and get a single generated constructor per partial class.
* Multi-project solutions get an automatically generated aggregator method in the host that chains every
  referenced project's registration method.
* Misuse is reported through first-class compiler diagnostics (`DIGEN###`), never through silent bad codegen.

Success = a consumer can `dotnet add package NkChinh.DI.Generator`, annotate classes, and call one generated
method in `Program.cs` — with no runtime assembly added to their dependency graph.

## Assumptions (validated design decisions)

| # | Assumption | Rationale |
|---|-----------|-----------|
| A1 | The generator assembly targets **netstandard2.0** and is compiled against **Roslyn 4.8** | Mandatory for source generators; Roslyn 4.8 = .NET 8 SDK baseline, so both net8.0 and net10.0 SDK consumers can load it. "net8.0/net10.0 support" is proven by tests & samples multi-targeting both TFMs. |
| A2 | Embedded attributes live in the **fixed namespace `DIGen`** (infrastructure marker in `DIGen.Generated`) | `ForAttributeWithMetadataName` requires a stable metadata name. A neutral namespace keeps the author prefix out of user code. Attributes are `internal`, emitted into every consuming project, opt-out via `DIGEN_EXCLUDE_ATTRIBUTES` define. |
| A3 | Generated extension classes live in **`Microsoft.Extensions.DependencyInjection`** namespace | Standard .NET convention → discoverable without extra `using`. Class name is assembly-unique so no collisions. |
| A4 | Per-project method registers **only that project's own services**; the aggregator (`Add{X}AllServices`) chains each referenced module **exactly once** | Prevents duplicate registrations in diamond dependency graphs. |
| A5 | License **MIT**, author `NkChinh`, repo URL `https://github.com/nkchinh/di-generator` (placeholder) | Standard OSS defaults; trivially changeable in one place. |
| A6 | Consuming projects must reference `Microsoft.Extensions.DependencyInjection.Abstractions` >= 8.0 | Required anyway to call `IServiceCollection`; keyed services & `ActivatorUtilitiesConstructor` need 8.0+. The generator package itself brings no dependency. |
| A7 | Required Scope Validation diagnostics continue the existing **`DIGEN`** numbering (`DIGEN008`–`DIGEN010`), same category `NkChinh.DI.Generator` | One diagnostic family for the whole generator, per project convention — no second prefix. |
| A8 | `RequiredScope`/`RequiredExternalScope` validation only covers registrations with an explicit `TService` (`[XxxService<T>]`, `[Service<T>]`) | Non-generic self-registration has no `TService` to look up a lock by. |
| A9 | `DiServiceScope`/`RequiredScopeAttribute`/`RequiredExternalScopeAttribute`/`ServiceAttribute<T>` are always embedded (no MEDI dependency); `DiServiceScopeExtensions.ToServiceLifetime()` is embedded **only** when the compilation can resolve `Microsoft.Extensions.DependencyInjection.ServiceLifetime` | Reconciles "must work in MEDI-free Domain/Application projects" with "must expose a mapping to MEDI's `ServiceLifetime`" — the mapping is only meaningful (and only compiles) where MEDI is already referenced, which the Host always does. |
| A10 | Conflicting `[assembly: RequiredExternalScope]` locks for the same type (different lifetimes, both reachable from the current compilation) are a compile **error** (DIGEN010), not a silent last-wins | Matches the feature's purpose — surface misconfiguration instead of guessing. |

## Tech Stack

* C# / .NET: generator `netstandard2.0`, `LangVersion=latest`; tests & samples `net8.0;net10.0`
* `Microsoft.CodeAnalysis.CSharp` 4.8.0 (`PrivateAssets=all`), `IIncrementalGenerator` only
* Tests: xUnit + `Verify.SourceGenerators` (snapshots) + in-memory `CSharpGeneratorDriver` + emitted-assembly execution (integration)

## Commands

```
Pack:        dotnet pack src/DI.Generator -c Release -o artifacts   (first — samples restore from ./artifacts)
Build:       dotnet build DI.Generator.slnx -c Release
Unit tests:  dotnet test tests/DI.Generator.Tests -c Release
All tests:   dotnet test DI.Generator.slnx -c Release
Sample run:  dotnet run --project samples/Sample.Host
```

## Project Structure

```
src/DI.Generator/                  Generator (packable, netstandard2.0)
tests/DI.Generator.Tests/          Unit + snapshot tests (GeneratorDriver, Verify)
tests/DI.Generator.IntegrationTests/  End-to-end: emit → load → resolve services (net8.0;net10.0)
samples/Sample.Domain|Infrastructure|Host/  Multi-project demo consuming the packed NuGet from ./artifacts
docs/                              SPEC, diagnostics reference, multi-project guide
.github/workflows/ci.yml           Build + test + pack
```

## Public Surface (embedded into consumer as `internal`)

Namespace `DIGen`:

* `SingletonServiceAttribute`, `ScopedServiceAttribute`, `TransientServiceAttribute` — self-registration; optional `string key` ctor arg → keyed service.
* `SingletonServiceAttribute<TService>`, `ScopedServiceAttribute<TService>`, `TransientServiceAttribute<TService>` — register as `TService`; optional key.
* `InjectAttribute` — on instance fields/properties of a `partial` class.
* `DiServiceScope` — `{ Singleton = 0, Scoped = 1, Transient = 2 }`. See [Required Scope Validation](#required-scope-validation).
* `RequiredScopeAttribute(DiServiceScope)` — on an interface, locks the lifetime any registration of that interface must use.
* `RequiredExternalScopeAttribute(Type, DiServiceScope)` — assembly-level; locks the lifetime for a type the current project doesn't own (third-party interface/`DbContext`/etc.).
* `ServiceAttribute<TService>` — registers the class as `TService` using whatever lifetime `TService` is locked to; optional key. Requires a lock to exist (see DIGEN008).

Conditionally embedded (only when the compilation can resolve `Microsoft.Extensions.DependencyInjection.ServiceLifetime`, i.e. the project already references MEDI — see A9):

* `DiServiceScopeExtensions.ToServiceLifetime(this DiServiceScope)` → `Microsoft.Extensions.DependencyInjection.ServiceLifetime`.

Namespace `DIGen.Generated` (infrastructure):

* `ServiceRegistrationModuleAttribute(string methodName, string extensionsTypeFqn)` — assembly-level marker emitted per module; consumed by referencing hosts to build the aggregator.

## Required Scope Validation

Prevents captive-dependency bugs (e.g. a `Scoped` `DbContext` registered as `Singleton`) with a compile-time lock-and-check mechanism.

**Locking a lifetime for a type:**
1. `[RequiredScope(DiServiceScope.Scoped)]` directly on an interface the project owns.
2. `[assembly: RequiredExternalScope(typeof(SomeThirdPartyType), DiServiceScope.Singleton)]` for a type the project doesn't own — declared in any project that references the third-party library, so the owning (e.g. Domain) project never needs that reference.

**Resolution precedence** when a registration names `TService`: (1) `TService`'s own `[RequiredScope]` wins; (2) else an `[assembly: RequiredExternalScope(typeof(TService), ...)]` visible from the current compilation — its own assembly attributes plus every referenced assembly's (same reachability the existing module-marker scan already uses, so it works transitively across project references); (3) else `TService` has no lock.

**Checks:**
* `[Service<TService>]` — auto-registers using the locked lifetime. No lock found → **DIGEN008** (use an explicit `[SingletonService<T>]`/`[ScopedService<T>]`/`[TransientService<T>]` instead).
* `[SingletonService<T>]` / `[ScopedService<T>]` / `[TransientService<T>]` — if `T` has a lock and the attribute's lifetime disagrees → **DIGEN009**.
* Two reachable `[assembly: RequiredExternalScope]` declarations lock the same type to different lifetimes → **DIGEN010**.

**Scope (v1 boundary):** only registrations with an explicit `TService` generic argument are checked. Non-generic self-registration (`[SingletonService]` with no `<T>`) has nothing to look up a lock by, and is out of scope.

## Generation Rules

### Service registration

* Per assembly, emit `public static class {SanitizedAssemblyName}ServiceCollectionExtensions` with
  `Add{SanitizedAssemblyName}Services(this IServiceCollection services)`.
  * `MyCompany.Infrastructure` → `AddMyCompanyInfrastructureServices`. Sanitization: split on non-alphanumeric, PascalCase-join, prefix `_` if leading digit.
* `[XxxService]` → `services.AddXxx<Impl>()`; `[XxxService<TService>]` → `services.AddXxx<TService, Impl>()`; key → `AddKeyedXxx(..., "key")`.
* `[Service<TService>]` → same as `[XxxService<TService>]`, but `Xxx` is resolved from `TService`'s locked scope instead of being spelled out — see [Required Scope Validation](#required-scope-validation).
* Classes implementing `Microsoft.Extensions.Hosting.IHostedService` → `services.AddHostedService<Impl>()`.
* Registrations sorted by implementation FQN → deterministic output.
* Abstract classes are skipped with a warning; a class may carry exactly one lifetime attribute.

### Constructor injection (`[Inject]`)

* All `[Inject]` members of a class are **grouped** → exactly **one** generated constructor in a partial declaration, decorated with `[ActivatorUtilitiesConstructor]`.
* Parameter naming: derived from the member **type** name — strip leading `I` when followed by uppercase, camelCase (`IOrderRepository` → `orderRepository`). Collisions fall back to camelCased member name (trimmed `_`), then numeric suffix. C# keywords escaped with `@`.
* Parameter order = member declaration order (file path, then position).
* Supports generic and nested classes (nested partial chain is emitted).

### Multi-project aggregation

* Every assembly that generated a registration method also emits the assembly-level module marker.
* Any project whose references contain markers gets `Add{X}AllServices(this IServiceCollection)` that invokes
  each referenced module's method once (sorted by method name) and finally its own `Add{X}Services` if present.

## Diagnostics

| ID | Severity | Trigger |
|----|----------|---------|
| DIGEN001 | Error | Class annotated `[XxxService<TService>]` does not implement/inherit `TService` |
| DIGEN002 | Error | `[Inject]` member's containing type (or a containing outer type) is not `partial` |
| DIGEN003 | Error | `[Inject]` on a static/const member |
| DIGEN004 | Error | `[Inject]` property cannot be assigned from a constructor (non-auto, no setter) |
| DIGEN005 | Warning | Lifetime attribute on an abstract class — registration skipped |
| DIGEN006 | Error | Multiple lifetime attributes on one class |
| DIGEN007 | Error | `[Inject]` inside a non-class type (struct/interface) |
| DIGEN008 | Error | `[Service<T>]` used but `T` has no locked scope (no `[RequiredScope]`, no reachable `[assembly: RequiredExternalScope]`) |
| DIGEN009 | Error | `[XxxService<T>]`'s lifetime disagrees with `T`'s locked scope |
| DIGEN010 | Error | Two reachable `[assembly: RequiredExternalScope]` declarations lock the same type to different lifetimes |

Category `NkChinh.DI.Generator`, `helpLinkUri` → docs/diagnostics.md anchors.

## Packaging

* `PackageId=NkChinh.DI.Generator`, `Version=0.0.0`, `DevelopmentDependency=true`, `IncludeBuildOutput=false`.
* DLL packed to `analyzers/dotnet/cs/`; no `lib/`, no dependencies (pure generator).
* README.md packed; MIT license expression; deterministic build; symbols not applicable (analyzer).

## Testing Strategy

* **Unit/snapshot** (`DI.Generator.Tests`): every generation rule and every diagnostic via `CSharpGeneratorDriver`
  + `Verify.SourceGenerators` snapshots; assembly-name sanitization table tests; parameter-naming table tests;
  cacheability smoke test (second run produces identical output).
* **Integration** (`DI.Generator.IntegrationTests`, net8.0 + net10.0): compile user code with the generator,
  emit to a real assembly, load it, execute the generated extension methods against a real `ServiceCollection`:
  basic/keyed/hosted resolution, `[Inject]` constructor actually receives services, and a two-assembly
  lib→host chain exercising the aggregator.
* **Samples** double as a compile-time acceptance test in CI (`dotnet run --project samples/Sample.Host`).

## Boundaries

* **Always:** deterministic output; `// <auto-generated/>` + `#nullable enable` headers; value-equatable pipeline models (no `ISymbol`/`SyntaxNode` retained); report diagnostics instead of emitting broken code; run full test suite before declaring done.
* **Ask first:** changing public attribute names/namespaces; adding runtime dependencies; raising the Roslyn baseline.
* **Never:** ship a `lib/` folder; throw from the generator on user input; register abstract/invalid classes silently.

## Success Criteria

1. `dotnet pack` produces a nupkg whose only content is `analyzers/dotnet/cs/NkChinh.DI.Generator.dll` (+ README/license metadata).
2. All unit, snapshot and integration tests pass on net8.0 and net10.0.
3. Sample multi-project solution builds and, at runtime, resolves services registered across three projects through the generated aggregator.
4. Every diagnostic in the table above has at least one test proving it fires (and one proving it doesn't misfire).
5. All project-specific naming from the legacy file is gone; naming derives from `AssemblyName` or is a documented fixed contract.
6. `[Service<T>]` correctly resolves its lifetime across project boundaries (interface locked in one project, registration in another, external-scope lock in a third) — proven by an integration test, not just a unit test.

## Open Questions

None blocking — assumptions A1–A10 stand unless the user overrides them. A7–A10 (Required Scope
Validation design) are freshly drafted and awaiting explicit confirmation before implementation
starts (see PR/commit that introduces this section).
