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
- 🔧 **`[Inject]` constructor generation** — annotate fields/properties; all `[Inject]` members of a partial class are grouped into **one** generated constructor with camelCase parameters and `[ActivatorUtilitiesConstructor]`.
- 🧩 **Multi-project aware** — every project generates its own `Add{Assembly}Services()`; the host additionally gets `Add{Assembly}AllServices()` that chains every referenced project exactly once.
- 🔒 **Required Scope Validation** — lock an interface's lifetime once with `[RequiredScope]` (or `[assembly: RequiredExternalScope]` for third-party types); `[Service<T>]` then resolves it automatically, and any explicit lifetime attribute that disagrees is a compile error — no more accidental captive dependencies (e.g. a `Scoped` `DbContext` registered as `Singleton`).
- 🚨 **First-class diagnostics** — misuse is a compile error (`DIGEN001`–`DIGEN010`), never silently wrong code.
- 📦 **Pure generator package** — ships only an analyzer assembly; no `lib/`, no runtime dependency added to your app.
- 🌱 **Trimming & Native AOT friendly** — registrations are plain `services.Add{Lifetime}<...>()` calls generated at compile time, not reflection over your assemblies at startup, so there's nothing for the trimmer to break and nothing incompatible with Native AOT.
- ⚡ **Fully incremental** (`IIncrementalGenerator`) — cache-friendly pipelines, fast IDE experience.

## Requirements

| | |
|---|---|
| Consuming project TFM | net8.0, net10.0 (any TFM whose SDK ships Roslyn ≥ 4.8, i.e. .NET SDK 8+) |
| Language version | C# 11+ for generic attributes (`[ScopedService<T>]`); non-generic attributes work on older versions |
| Runtime package | Your project needs `Microsoft.Extensions.DependencyInjection.Abstractions` ≥ 8.0 (you already have it if you call `IServiceCollection`) |

## Installation

```xml
<ItemGroup>
  <PackageReference Include="NkChinh.DI.Generator" Version="0.0.1" PrivateAssets="all" />
</ItemGroup>
```

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
[global::Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
public OrderProcessor(IOrderRepository orderRepository, IPaymentGateway paymentGateway)
{
    this._repository = orderRepository;
    this._gateway = paymentGateway;
}
```

Parameter names are derived from the member's **type** name (`IOrderRepository` → `orderRepository`,
leading `I` stripped, camelCased). When two members share a type, names fall back to the member
names (`_primary` → `primary`). C# keywords are handled automatically.

### Multi-project solutions

Install the package in **every** project that declares services. Each project generates its own
extension method named after its `AssemblyName`:

| Project (AssemblyName) | Generated method |
|---|---|
| `MyCompany.Domain` | `AddMyCompanyDomainServices()` |
| `MyCompany.Infrastructure` | `AddMyCompanyInfrastructureServices()` |
| `MyCompany.Api` (host) | `AddMyCompanyApiServices()` + `AddMyCompanyApiAllServices()` |

The host's `Add…AllServices()` chains every referenced project's method exactly once (including
transitive references, deduplicated — safe for diamond dependency graphs), then the host's own
services:

```csharp
builder.Services.AddMyCompanyApiAllServices(); // one call registers everything
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
   (`[RequiredScope]` / `[assembly: RequiredExternalScope]`) → emits `Add{Assembly}Services()` in
   the `Microsoft.Extensions.DependencyInjection` namespace, plus an assembly-level module marker.
3. Reads module markers from referenced assemblies → emits `Add{Assembly}AllServices()`.
4. Groups `[Inject]` members per class → emits one constructor per partial class.

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
