---
name: di-generator
description: Integrate and use NkChinh.DI.Generator effectively in .NET applications. Use this skill whenever a user wants to install or configure DIGen, register services with SingletonService/ScopedService/TransientService/Service attributes, generate constructors with [Inject], use keyed or optional injection, enforce lifetimes with RequiredScope, compose dependency injection across multiple projects, troubleshoot DIGEN diagnostics, or inspect generated IServiceCollection registration methods. This skill is for consumers of the generator, not contributors modifying its implementation.
license: MIT
compatibility: Requires .NET SDK 8 or newer. Generic service attributes require C# 11 or newer.
metadata:
  author: NkChinh
  version: "0.0.6"
  repository: https://github.com/nkchinh/di-generator
  source: https://github.com/nkchinh/di-generator/tree/master/skills/di-generator
---

# Use NkChinh.DI.Generator

Apply DIGen to application code with the smallest registration setup that preserves correct service
lifetimes, constructor activation, and multi-project composition.

## Start by Inspecting the Solution

Before editing, identify:

1. Which projects declare services or use `[Inject]`.
2. Which projects reference `Microsoft.Extensions.DependencyInjection` (MEDI).
3. Which project is the composition root, such as an ASP.NET Core host, worker, or console app.
4. Existing registration methods and explicit `IServiceCollection` calls that could duplicate generated registrations.
5. Lifetime-sensitive dependencies such as `DbContext`, request state, caches, and hosted services.

Do not edit generated `.g.cs` files. Change attributes, project references, or user source instead.

## Install the Generator

Add the analyzer package to every project that declares DIGen services or uses `[Inject]`:

```xml
<ItemGroup>
  <PackageReference Include="NkChinh.DI.Generator" Version="0.0.6" PrivateAssets="all" />
</ItemGroup>
```

`PrivateAssets="all"` keeps the analyzer from becoming a transitive runtime dependency. Analyzer
references do not flow transitively, so install it in each participating project.

Only projects that call generated `IServiceCollection` methods need
`Microsoft.Extensions.DependencyInjection.Abstractions` 8.0 or newer. Domain/Application projects
can publish service definitions without referencing MEDI.

## Choose a Registration Attribute

Use the narrowest suitable registration:

| Intent | Attribute |
|---|---|
| One shared instance for the application/container | `[SingletonService]` or `[SingletonService<T>]` |
| One instance per DI scope/request | `[ScopedService]` or `[ScopedService<T>]` |
| A new instance each resolution | `[TransientService]` or `[TransientService<T>]` |
| Derive lifetime from a locked service contract | `[Service<T>]` |
| Register with a key | Any lifetime attribute with a string key |

Register against an interface when consumers should depend on an abstraction:

```csharp
using DIGen;

public interface IOrderRepository { }

[ScopedService<IOrderRepository>]
public sealed class OrderRepository : IOrderRepository { }
```

Use the non-generic attribute for concrete self-registration:

```csharp
[SingletonService]
public sealed class SystemClock { }
```

### Lifetime Guidance

- Choose `Singleton` only for thread-safe services that do not capture scoped dependencies.
- Choose `Scoped` for request/unit-of-work state and services depending on scoped resources such as `DbContext`.
- Choose `Transient` for lightweight stateless services when independent instances are appropriate.
- Do not fix lifetime diagnostics by arbitrarily changing attributes. Follow the lifetime required by the dependency graph.
- Multiple implementations of one interface are valid; do not remove them merely because the service type repeats.

## Lock Required Lifetimes

Use `[RequiredScope]` on an interface when every implementation must use the same lifetime:

```csharp
[RequiredScope(DiServiceScope.Scoped)]
public interface IOrderRepository { }

[Service<IOrderRepository>]
public sealed class SqlOrderRepository : IOrderRepository { }
```

Use `[assembly: RequiredExternalScope]` when the contract is defined in a third-party assembly:

```csharp
using DIGen;

[assembly: RequiredExternalScope(
    typeof(StackExchange.Redis.IConnectionMultiplexer),
    DiServiceScope.Singleton)]
```

Prefer `[Service<T>]` after locking the contract so implementations cannot accidentally select an
incompatible lifetime.

## Generate Constructor Injection

Mark fields or assignable properties with `[Inject]` and make the containing class, plus every
containing outer class, `partial`:

```csharp
[TransientService<IOrderProcessor>]
public sealed partial class OrderProcessor : IOrderProcessor
{
    [Inject] private readonly IOrderRepository _repository;
    [Inject] private readonly ILogger<OrderProcessor> _logger;
}
```

DIGen groups all `[Inject]` members into one generated constructor. Prefer `readonly` fields or
get-only auto-properties so dependencies remain immutable after construction.

Do not apply `[Inject]` to static/const members or expression-bodied/read-only computed properties.

### Existing Constructors

If the class also has user-defined constructors, DIGen emits a factory registration that explicitly
uses the generated injection constructor. Keep every required `[Inject]` dependency registered;
otherwise resolution fails at runtime and may produce `DIGEN011`.

### Optional Dependencies

In nullable-enabled projects, express optional injection with a nullable reference type:

```csharp
[Inject] private readonly ITelemetry? _telemetry;
```

Do not add `?` merely to suppress `DIGEN011`; use it only when absence is valid application behavior.
In nullable-disabled projects, an initializer is the optional signal.

### Keyed Services

Register and inject with the same key:

```csharp
[SingletonService<IPaymentGateway>("stripe")]
public sealed class StripeGateway : IPaymentGateway { }

[Inject("stripe")]
private readonly IPaymentGateway _gateway;
```

Keys are exact strings. Check spelling and casing on both registration and injection sites.

## Compose Multiple Projects

Install DIGen in every project declaring services. Each project publishes service metadata even if it
does not reference MEDI.

A project with MEDI generates:

- `Add{Assembly}OwnedServices()`: registers only services owned by that assembly.
- `Add{Assembly}Services()`: composition-root entry point for its complete reachable project graph.

At application startup, call only the root project's `Add{Assembly}Services()` method:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMyCompanyApiServices();
```

The method name derives from `<AssemblyName>`, sanitized into a C# identifier. For example,
`MyCompany.Api` becomes `AddMyCompanyApiServices`.

Do not also call dependency projects' `Add...Services()` or `Add...OwnedServices()` methods manually;
the root already composes them and extra calls duplicate registrations.

### Visibility Rules

- A project with MEDI registers its own services inside its assembly, so its service interfaces and implementations may be `internal`.
- A project without MEDI relies on another assembly to emit its registration. Its published service and implementation types should be `public`, unless the root has access through `InternalsVisibleTo`.
- `DIGEN013` means an inaccessible MEDI-free definition was skipped. The host builds, but that service is not registered.

### Registration Precedence

Generated root methods register dependencies first and root-owned services last. This preserves
MEDI's last-registration-wins behavior for single-service resolution. Be deliberate when the root and
a dependency register the same service type; `IEnumerable<T>` still exposes all registrations.

Diamond dependency graphs are deduplicated for shared MEDI-free definitions.

## Handle Diagnostics

Fix the cause rather than suppressing diagnostics:

| ID | Response |
|---|---|
| `DIGEN001` | Make the implementation implement/inherit the generic service type, or correct the attribute type. |
| `DIGEN002` | Mark the class and all containing classes `partial`. |
| `DIGEN003` | Move `[Inject]` from a static/const member to an instance member. |
| `DIGEN004` | Use a field or assignable auto-property. |
| `DIGEN005` | Put the service attribute on a concrete implementation, not an abstract class. |
| `DIGEN006` | Keep exactly one service/lifetime attribute on the class. |
| `DIGEN007` | Use `[Inject]` inside a class, not a struct/interface. |
| `DIGEN008` | Add `[RequiredScope]`/`RequiredExternalScope`, or use an explicit lifetime attribute. |
| `DIGEN009` | Match the explicit registration lifetime to the locked lifetime, preferably via `[Service<T>]`. |
| `DIGEN010` | Remove conflicting external lifetime declarations for the same type. |
| `DIGEN011` | Register the required dependency, or make it genuinely optional with nullable syntax. |
| `DIGEN012` | In a MEDI-free project, make published types public or grant root access. MEDI projects may use internal types. |
| `DIGEN013` | Make the skipped MEDI-free types public, add `InternalsVisibleTo`, or let that project reference MEDI and own its registration. |

## Verify the Integration

After changes:

1. Build the complete solution, not only the edited project.
2. Run existing tests.
3. Confirm startup calls exactly one root `Add{Assembly}Services()` method.
4. Resolve representative services through a real `ServiceProvider` when tests permit.
5. Inspect generated code only when troubleshooting:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

Exclude that output directory from normal compilation if the project setup would compile generated
files twice. Never commit manual edits to generated output as the fix.

## Expected Agent Behavior

When asked to add or fix DI in a consumer repository:

1. Inspect project references, package references, service attributes, and the composition root.
2. State the lifetime and visibility implications of the chosen approach.
3. Make the minimal changes in user-owned source and project files.
4. Build and test the full affected project graph.
5. Report generated method names, diagnostics resolved, and any runtime resolution risks that remain.
