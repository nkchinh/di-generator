# NkChinh.DI.Generator

[![CI](https://github.com/nkchinh/di-generator/actions/workflows/ci.yml/badge.svg)](https://github.com/nkchinh/di-generator/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/NkChinh.DI.Generator.svg)](https://www.nuget.org/packages/NkChinh.DI.Generator)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A **pure Roslyn source generator** for `Microsoft.Extensions.DependencyInjection`:
attribute-driven service registration, `[Inject]` constructor generation, and automatic
multi-project registration chaining — with **zero runtime dependencies**. Everything the
package needs is generated into your project as `internal` code at compile time.

> Tài liệu tiếng Việt: [README.vi.md](README.vi.md)

## Features

- 🏷️ **Attribute-driven registration** — `[SingletonService]`, `[ScopedService<T>]`, `[TransientService]`, with optional keys for keyed services and automatic `AddHostedService` for `IHostedService` implementations.
- 🔧 **`[Inject]` constructor generation** — annotate fields/properties; all `[Inject]` members of a partial class are grouped into **one** generated constructor with camelCase parameters. Optional keys (`[Inject("key")]`) request keyed dependencies; nullable members (`T?`) become **optional** and resolve to `null` when the service isn't registered. In nullable-disabled projects, an initializer is used as the equivalent optional signal. The generator emits a **factory delegate** whenever constructor selection, keyed lookup, or optional lookup requires it — no `[ActivatorUtilitiesConstructor]` heuristic and no MEDI dependency required for the class's project to compile.
- 🔑 **`[Inject("key")]`** — optionally keyed; a registered service gets a generated factory using
  `GetRequiredKeyedService`/`GetKeyedService`, even when the class has no user constructor.
- 🛡️ **Compile-time safety nets** — `DIGEN011` warns when a non-optional `[Inject]` parameter's type
  isn't visibly registered in the current assembly or in any referenced project's published
  definitions (factory-delegate path only).
- 🧩 **Multi-project aware** — every project with services publishes assembly-level `ServiceDefinition`
  attributes. MEDI projects register their own services inside their assembly, including `internal`
  types; a root `Add{Assembly}Services()` call composes every reachable module and MEDI-free project.
- 🧬 **Works in projects with no MEDI reference at all** — a Domain/Application project that only declares interfaces and self-registers via `[Service<T>]`/lifetime attributes compiles cleanly with zero dependency on `Microsoft.Extensions.DependencyInjection`; the `IServiceCollection`-based methods appear only where a project actually references MEDI.
- 🔒 **Required Scope Validation** — lock an interface's lifetime once with `[RequiredScope]` (or `[assembly: RequiredExternalScope]` for third-party types); `[Service<T>]` then resolves it automatically, and any explicit lifetime attribute that disagrees is a compile error — no more accidental captive dependencies (e.g. a `Scoped` `DbContext` registered as `Singleton`).
- 🚨 **First-class diagnostics** — misuse is a compile error (`DIGEN001`–`DIGEN010`), and risky-but-valid constructions surface as warnings (`DIGEN011`), never silently wrong code.
- 📦 **Pure generator package** — ships only an analyzer assembly; no `lib/`, no runtime dependency added to your app.
- 🌱 **Trimming & Native AOT friendly** — registrations are plain `services.Add{Lifetime}<...>()` calls generated at compile time, not reflection over your assemblies at startup, so there's nothing for the trimmer to break and nothing incompatible with Native AOT.
- ⚡ **Fully incremental** (`IIncrementalGenerator`) — cache-friendly pipelines, fast IDE experience.

## Requirements

| | |
|---|---|
| Consuming project TFM | net8.0, net10.0 (any TFM whose SDK ships Roslyn ≥ 4.8, i.e. .NET SDK 8+) |
| Language version | C# 11+ for generic attributes (`[ScopedService<T>]`); non-generic attributes work on older versions |
| Runtime package | Only needed where you call `IServiceCollection`: `Microsoft.Extensions.DependencyInjection.Abstractions` ≥ 8.0. A Domain/Application project with no MEDI reference at all still compiles — see [How it works](#how-it-works). |

## Installation

```xml
<ItemGroup>
  <PackageReference Include="NkChinh.DI.Generator" Version="0.0.6" PrivateAssets="all" />
</ItemGroup>
```

### Agent Skill

Install the skill to help AI agents integrate and use DIGen effectively: choose service lifetimes,
configure `[Inject]`, compose multi-project registrations, and resolve diagnostics:

```bash
npx skills add nkchinh/di-generator --skill di-generator
# or
pnpm dlx skills add nkchinh/di-generator --skill di-generator
```

The skill is also available directly at [skills/di-generator/SKILL.md](skills/di-generator/SKILL.md).

## Quick start

```csharp
using DIGen;

public interface IOrderRepository { /* ... */ }

// Register as IOrderRepository, scoped lifetime
[ScopedService<IOrderRepository>]
public class OrderRepository : IOrderRepository { /* ... */ }

// Register as concrete type, singleton lifetime
[SingletonService]
public class MemoryCache { }

// Keyed service (requires .NET 8 DI)
[SingletonService<IPaymentGateway>("stripe")]
public class StripeGateway : IPaymentGateway { /* ... */ }
```

```csharp
// Program.cs — the method name derives from your AssemblyName:
// "MyCompany.Api" → AddMyCompanyApiServices()
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMyCompanyApiServices();
```

### Constructor injection with `[Inject]`

```csharp
using DIGen;

[TransientService<IOrderProcessor>]
public partial class OrderProcessor : IOrderProcessor
{
    [Inject] private readonly IOrderRepository _repository;
    [Inject] private readonly IPaymentGateway _gateway;
}
```

The generator emits **one** constructor for the class:

```csharp
public OrderProcessor(IOrderRepository orderRepository, IPaymentGateway paymentGateway)
{
    this._repository = orderRepository;
    this._gateway = paymentGateway;
}
```

Parameter names are derived from the member's **type** name (`IOrderRepository` → `orderRepository`,
leading `I` stripped, camelCased). When two members share a type, names fall back to the member
names (`_primary` → `primary`). C# keywords are handled automatically.

#### User-defined constructors → factory delegate

When a class with `[Inject]` members also declares one or more user constructors, the container's
default selector might pick the user ctor instead of the generated one — leaving fields unassigned.
The generator avoids this by emitting a **factory delegate** rather than a plain
`ServiceDescriptor(Type, Type, ServiceLifetime)`, so the generated `[Inject]` ctor is always the one
that runs:

```csharp
[ScopedService<IReportService>]
public partial class ReportService : IReportService
{
    [Inject] private readonly IOrderRepository _repository;

    // The presence of a user ctor switches on the factory-delegate registration:
    public ReportService(IReportOptions options) { /* ... */ }
}
// → registrations.Add((..., sp => new ReportService(
//       InjectServiceResolver.GetRequired<IOrderRepository>(sp))));
```

The delegate uses only `System.IServiceProvider` and the always-embedded `InjectServiceResolver`
helper — both are BCL-only — so the factory compiles and runs even in a Domain project that has no
reference to MEDI at all.

#### Optional `[Inject]` members

In a nullable-enabled project, an `[Inject]` member is optional only when annotated **nullable**
(`T?`). In a nullable-disabled project, where `T?` cannot express that contract, an initializer is
used as the optional signal. Optional members resolve through `IServiceProvider.GetService` (returns
`null` when missing); non-optional members use `GetRequired<T>` and throw when missing:

```csharp
[Inject] private readonly IOrderRepository _repository;   // required — throws if missing
[Inject] private readonly ITelemetryInitializer? _telemetry;  // optional — null if missing
```

A non-optional member whose type the generator can't see registered in the **current assembly** is
reported as **`DIGEN011`** — referenced-assembly registrations are resolvable at runtime and are
*not* reported (the check is intentionally local to avoid cross-project false positives).

#### Keyed `[Inject]` — honored on the factory-delegate path

```csharp
[Inject("primary")] private readonly ICache _primaryCache;
```

`InjectAttribute` accepts an optional key (`[Inject("key")]`). When the containing class is
registered as a service, the generated factory resolves the member **with the key** via
`GetRequiredKeyedService`/`GetKeyedService`, even if the class has no user-defined constructor. A key
alone never produces a warning.

### Multi-project solutions

Install the package in **every** project that declares services. Every project with services
publishes assembly-level `ServiceDefinition` attributes (no MEDI reference needed). The host's
generated extension method — named after its `AssemblyName` — registers its own services **plus**
every service published by referenced projects:

| Project (AssemblyName) | Generated method |
|---|---|
| `MyCompany.Domain` | publishes definitions (no MEDI reference) |
| `MyCompany.Infrastructure` | publishes definitions, `AddMyCompanyInfrastructureServices()` |
| `MyCompany.Api` (host) | `AddMyCompanyApiServices()` — own + referenced, exactly once |

Each MEDI project emits `Add{Assembly}OwnedServices()` for its own services and an
`Add{Assembly}Services()` root entry point. The root composes owned-module methods and directly
registers the union of MEDI-free definitions once (safe for diamond dependency graphs), so one call
registers everything across the whole graph:

```csharp
builder.Services.AddMyCompanyApiServices(); // one call registers everything
```

See [docs/multi-project.md](docs/multi-project.md) for details and the [samples](samples) folder
for a working three-project solution.

### Required Scope Validation

Lock an interface's lifetime once, then never worry about a class registering it with the wrong
one (the classic captive-dependency bug — a `Scoped` repository accidentally registered as
`Singleton`):

```csharp
using DIGen;

// Locks IOrderRepository to Scoped — declared once, wherever the interface lives.
[RequiredScope(DiServiceScope.Scoped)]
public interface IOrderRepository { /* ... */ }

// Resolves its lifetime from the lock automatically — no lifetime to get wrong.
[Service<IOrderRepository>]
public class SqlOrderRepository : IOrderRepository { /* ... */ }

// A mismatched explicit attribute is a compile error (DIGEN009):
[SingletonService<IOrderRepository>]   // error: locked to Scoped, not Singleton
public class Wrong : IOrderRepository { /* ... */ }
```

For a type you don't own (a third-party interface, a `DbContext`, `StackExchange.Redis.IConnectionMultiplexer`, …),
lock it from whichever project already references that library — the owning project never needs
the dependency:

```csharp
// In the project that references StackExchange.Redis:
[assembly: RequiredExternalScope(typeof(IConnectionMultiplexer), DiServiceScope.Singleton)]
```

`[RequiredScope]` on the type itself always wins if both are present. See
[docs/diagnostics.md](docs/diagnostics.md) for `DIGEN008`–`DIGEN010`.

## Attributes reference

All attributes live in the `DIGen` namespace and are embedded into your project as `internal`
types (no runtime dependency):

| Attribute | Registration |
|---|---|
| `[SingletonService]` / `[ScopedService]` / `[TransientService]` | `services.Add{Lifetime}<Impl>()` |
| `[SingletonService<TService>]` (and Scoped/Transient) | `services.Add{Lifetime}<TService, Impl>()` |
| `[...Service("key")]` | `services.AddKeyed{Lifetime}(...)` |
| any of the above on an `IHostedService` implementation | `services.AddHostedService<Impl>()` |
| `[Inject]` on a field/property | parameter of the single generated constructor |
| `[Inject("key")]` | keyed resolution through a generated factory whenever the containing class is registered |
| `[Inject]` on a `T?` member | optional parameter — resolves to `null` when the service isn't registered; nullable-disabled projects use an initializer as the optional signal |
| `[RequiredScope(DiServiceScope)]` on an interface | locks the lifetime any registration of it must use |
| `[assembly: RequiredExternalScope(typeof(T), DiServiceScope)]` | locks the lifetime for a type you don't own |
| `[Service<TService>]` | registers using whatever lifetime `TService` is locked to |

## Diagnostics

| ID | Severity | Meaning |
|----|----------|---------|
| `DIGEN001` | Error | Class doesn't implement/inherit the service type in its generic attribute |
| `DIGEN002` | Error | `[Inject]` class (or a containing type) is not `partial` |
| `DIGEN003` | Error | `[Inject]` on a static/const member |
| `DIGEN004` | Error | `[Inject]` property can't be assigned from a constructor |
| `DIGEN005` | Warning | Lifetime attribute on an abstract class (ignored) |
| `DIGEN006` | Error | Multiple lifetime attributes on one class |
| `DIGEN007` | Error | `[Inject]` inside a non-class type |
| `DIGEN008` | Error | `[Service<T>]` used but `T` has no locked scope |
| `DIGEN009` | Error | An explicit lifetime attribute disagrees with `T`'s locked scope |
| `DIGEN010` | Error | Two `[assembly: RequiredExternalScope]` declarations lock the same type differently |
| `DIGEN011` | Warning | Non-optional `[Inject]` member's type not registered in the current assembly or any referenced project's published definitions (factory-delegate path only) |

Full descriptions and fixes: [docs/diagnostics.md](docs/diagnostics.md).

## Configuration

- **Opt out of embedded attributes** (e.g. `InternalsVisibleTo` conflicts): define
  `DIGEN_EXCLUDE_ATTRIBUTES` in `<DefineConstants>` and provide the types yourself.
- **Inspect generated code**:

  ```xml
  <PropertyGroup>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
  </PropertyGroup>
  ```

## How it works

The package is an analyzer-only NuGet (`analyzers/dotnet/cs/`). At compile time it:

1. Embeds the `DIGen` attributes as `internal` types into your compilation.
2. Scans classes with lifetime attributes → resolves `[Service<T>]` and validates locked scopes
   (`[RequiredScope]` / `[assembly: RequiredExternalScope]`) → emits one `[assembly: ServiceDefinition]`
   per service in the owning project, and reads the definitions published by every referenced
   assembly — **no reference to MEDI required for this step**. A `ServiceDefinition` carries only
   framework-typed data (implementation/service types, lifetime, key, hosted flag, `[Inject]` member
   metadata), so it is portable across project references.
3. **Only if your project resolves MEDI**, additionally emits `Add{Assembly}OwnedServices(this IServiceCollection)`
   for services owned by that assembly and `Add{Assembly}Services(this IServiceCollection)` as a root
   entry point. The root calls each reachable MEDI module's owned method, so those modules can register
   `internal` service types, then directly registers definitions from MEDI-free assemblies once. Classes with `[Inject]` members
   **and** a user-defined constructor are activated through a generated factory delegate
   (`sp => new T(...)`, resolving each member via the embedded `InjectServiceResolver`, keyed members
   via `GetRequiredKeyedService`/`GetKeyedService`).
4. Groups `[Inject]` members per class → emits one constructor per partial class. When the class
   requires explicit activation (user constructor, keyed member, or optional member), its published definition tells the host to emit a factory delegate
   (`sp => new T(InjectServiceResolver.GetRequired/GetOptional<...>(sp), ...)`) using only BCL
   types; when the class has `[Inject]` only and no user ctor, the standard
   `(Type, Type, ServiceLifetime)` descriptor is used (`Factory = null`).

Because the generator itself targets `netstandard2.0` and compiles against Roslyn 4.8, it works
with the .NET 8 SDK and newer (including .NET 10).

## Development

```bash
dotnet pack src/DI.Generator -c Release -o artifacts   # pack first — samples restore the package from ./artifacts
dotnet build DI.Generator.slnx -c Release   # build
dotnet test DI.Generator.slnx -c Release    # unit + snapshot + integration tests (net8.0 & net10.0)
dotnet run --project samples/Sample.Host   # end-to-end sample
```

The samples reference `NkChinh.DI.Generator` as a real `PackageReference` restored from the local
`artifacts` feed, so they double as an acceptance test of the packed NuGet. After changing the
generator, repack and clear the cached package before rebuilding the samples:

```bash
dotnet pack src/DI.Generator -c Release -o artifacts
dotnet nuget locals global-packages --clear   # or delete ~/.nuget/packages/nkchinh.di.generator
```

The project is spec-driven ([docs/SPEC.md](docs/SPEC.md)) and test-driven: snapshot tests via
Verify.SourceGenerators, behavior tests via `CSharpGeneratorDriver`, and integration tests that
emit, load, and execute the generated code against a real `ServiceCollection`.

## License

[MIT](LICENSE) © NkChinh
