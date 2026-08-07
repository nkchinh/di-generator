# Changelog

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **`[Inject("key")]` accepted** — `InjectAttribute` takes an optional key as a compile-time
  signal. In a project with no reference to `Microsoft.Extensions.DependencyInjection` the key is
  reported as `DIGEN012` and ignored at runtime (the member resolves by type). The generator does not
  currently emit keyed lookup for `[Inject]` members.
- **Optional `[Inject]` members** — a member annotated nullable (`T?`) or with a default value
  (`= null` / `= default`) is resolved via `IServiceProvider.GetService` (returns `null` when the
  service isn't registered) instead of the default `GetRequired<T>` (throws when missing).
- **Factory-delegate activation** — when a class has `[Inject]` members **and** a user-defined
  constructor, the registration now carries a factory delegate (`sp => new T(...)`) using only BCL
  types (`System.IServiceProvider` + embedded `InjectServiceResolver`) so the container always
  activates the generated `[Inject]` constructor. The generated constructor is no longer decorated
  with `[ActivatorUtilitiesConstructor]`, and the class's own project does not need an MEDI
  reference for the factory to compile. Single-constructor classes keep the standard
  `(Type, Type, ServiceLifetime)` descriptor.
- Registration tuple gained a 6th field, `Func<IServiceProvider, object>? Factory`, carrying the
  factory delegate above (`null` otherwise). Framework-types-only, so `Collect{X}Services` remains
  callable across MEDI-free project references.
- Diagnostics `DIGEN011` (non-optional `[Inject]` member's type not registered in the current
  assembly — factory-delegate path only; referenced-assembly registrations are resolvable at runtime
  and not reported) and `DIGEN012` (`[Inject("key")]` in a project without an MEDI reference; the
  key is ignored at runtime).

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
