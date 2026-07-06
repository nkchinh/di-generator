# Changelog

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
- `DiServiceScopeExtensions.ToServiceLifetime()`, embedded only in projects that already
  reference `Microsoft.Extensions.DependencyInjection` (so the core `DiServiceScope` enum keeps
  working in MEDI-free Domain/Application projects).

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
