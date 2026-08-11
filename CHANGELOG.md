# Changelog

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **`[Inject("key")]` honored through generated factories** — `InjectAttribute` takes an optional
  key. When the containing class is registered, the generated factory resolves the member with the
  key via `GetRequiredKeyedService`/`GetKeyedService`, even without a user-defined constructor. A key
  alone no longer produces a warning (`DIGEN012` removed).
- **Optional `[Inject]` members** — in nullable-enabled projects, a member annotated `T?` resolves
  via `IServiceProvider.GetService` (returns `null` when missing); in nullable-disabled projects an
  initializer is accepted as the equivalent optional signal. Non-nullable members in nullable-aware
  projects remain required even when initialized, so generated factories never violate their type contract.
- **Factory-delegate activation** — when a class has `[Inject]` members **and** a user-defined
  constructor, the registration uses a factory delegate (`sp => new T(...)`) using only BCL
  types (`System.IServiceProvider` + embedded `InjectServiceResolver`) so the container always
  activates the generated `[Inject]` constructor. The generated constructor is no longer decorated
  with `[ActivatorUtilitiesConstructor]`, and the class's own project does not need an MEDI
  reference for the factory to compile. Single-constructor classes keep the standard
  `(Type, Type, ServiceLifetime)` descriptor.
- Keyed or optional `[Inject]` members now also require factory activation, because standard MEDI
  constructor activation cannot apply their published key/optionality metadata.
- Diagnostic `DIGEN011` (non-optional `[Inject]` member's type not registered in the current
  assembly **or in any reachable referenced assembly's published `ServiceDefinition`s** —
  factory-delegate path only).
- Diagnostic `DIGEN012` warns when a published service interface or implementation is not public
  and may be inaccessible to generated registrations in another assembly.
- Diagnostic `DIGEN013` warns in the host when an inaccessible referenced service definition is
  skipped instead of producing uncompilable generated registration code.

### Changed

- **Owned-module multi-project registration** — MEDI projects now emit
  `Add{Assembly}OwnedServices()` for registrations compiled in their owning assembly, allowing
  `internal` services to participate safely. `Add{Assembly}Services()` remains the root entry point,
  composes reachable MEDI modules, and directly registers deduplicated MEDI-free definitions.
- **Published-definition cross-project model** — every project with services now emits one
  `[assembly: DIGen.Generated.ServiceDefinition]` per service (implementation/service types,
  resolved lifetime, key, hosted flag, `[Inject]` member metadata) regardless of any MEDI reference.
  A MEDI-having host reads the definitions published by every reachable referenced assembly and
  generates **direct** `services.Add{...}` calls for its own services plus the referenced ones in a
  single `Add{Assembly}Services(this IServiceCollection)`. The `Collect{Assembly}Services` /
  `Add{Assembly}AllServices` module-marker aggregation and the `MaterializeServices` runtime helper
  are **removed**. Cross-assembly sharing is now a compile-time contract instead of a runtime
  indirection layer; diamond dependency graphs stay safe because each referenced assembly's
  definitions are consumed exactly once per host compilation. Consumers call
  `Add{Assembly}Services()` (no separate aggregator method).

## [0.0.1] - 2026-07-06

### Added

- **Required Scope Validation**: `[RequiredScope(DiServiceScope)]` locks the lifetime an interface
  you own must be registered with; `[assembly: RequiredExternalScope(typeof(T), DiServiceScope)]`
  does the same for a type you don't own, declared from any project that references it.
  `[Service<TService>]` registers a class using whatever lifetime `TService` is locked to, with no
  lifetime to spell out (or get wrong). An explicit lifetime attribute that disagrees with a locked
  scope is a compile error.
- Diagnostics `DIGEN008` (`[Service<T>]` with no locked scope), `DIGEN009` (explicit lifetime
  disagrees with the lock), `DIGEN010` (conflicting `[assembly: RequiredExternalScope]`
  declarations for the same type reachable from one compilation).
- Registration is now split into **Collect** (always emitted, no MEDI reference needed —
  `Collect{Assembly}Services` builds a list of registrations as a framework-types-only tuple) and
  **Materialize** (emitted only where the project resolves MEDI — applies the list to a real
  `IServiceCollection`). This means a Domain/Application project that self-registers via
  `[Service<T>]`/lifetime attributes compiles with **zero reference** to
  `Microsoft.Extensions.DependencyInjection`, not just one that carries `[RequiredScope]` markers.
  `Add{Assembly}Services`/`Add{Assembly}AllServices` are unchanged from the outside — this is an
  internal restructuring, not a breaking change to the generated public API.
- `DiServiceScopeExtensions.ToServiceLifetime()`, embedded only in projects that already
  reference `Microsoft.Extensions.DependencyInjection`.

## [0.0.0] - 2026-07-03

Initial release.

### Added

- Attribute-driven service registration: `[SingletonService]`, `[ScopedService]`,
  `[TransientService]` (self-registration) and generic `[XxxService<TService>]` variants,
  with optional key argument for keyed services.
- Automatic `AddHostedService` registration for `IHostedService` implementations.
- Per-assembly generated extension method `Add{AssemblyName}Services(this IServiceCollection)`.
- `[Inject]` attribute for fields/properties: one generated constructor per partial class,
  camelCase parameter naming derived from member types, decorated with
  `[ActivatorUtilitiesConstructor]`.
- Multi-project support: assembly-level module markers and a generated
  `Add{AssemblyName}AllServices()` aggregator chaining all referenced modules exactly once.
- Diagnostics `DIGEN001`–`DIGEN007` (see docs/diagnostics.md).
- Pure-generator packaging: analyzer-only NuGet with zero runtime dependencies;
  attributes embedded as `internal` types (opt-out via `DIGEN_EXCLUDE_ATTRIBUTES`).

[Unreleased]: https://github.com/nkchinh/di-generator/compare/v0.0.1...HEAD
[0.0.1]: https://github.com/nkchinh/di-generator/compare/v0.0.0...v0.0.1
[0.0.0]: https://github.com/nkchinh/di-generator/releases/tag/v0.0.0
