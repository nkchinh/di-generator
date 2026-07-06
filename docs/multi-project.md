# Multi-project architecture

## How it works

Every project that installs `NkChinh.DI.Generator` and declares at least one service gets:

1. A public extension method registering **only that project's own services**:

   ```csharp
   // AssemblyName = MyCompany.Infrastructure
   namespace Microsoft.Extensions.DependencyInjection
   {
       public static class MyCompanyInfrastructureServiceCollectionExtensions
       {
           public static IServiceCollection AddMyCompanyInfrastructureServices(
               this IServiceCollection services) { /* ... */ }
       }
   }
   ```

2. An assembly-level **module marker** (internal infrastructure attribute):

   ```csharp
   [assembly: DIGen.Generated.ServiceRegistrationModuleAttribute(
       "AddMyCompanyInfrastructureServices",
       "Microsoft.Extensions.DependencyInjection.MyCompanyInfrastructureServiceCollectionExtensions")]
   ```

Any project whose compilation references assemblies carrying module markers (directly or
transitively — reference closure is flattened) additionally gets an **aggregator**:

```csharp
public static IServiceCollection AddMyCompanyApiAllServices(this IServiceCollection services)
{
    // every referenced module, exactly once, deterministic order
    MyCompanyDomainServiceCollectionExtensions.AddMyCompanyDomainServices(services);
    MyCompanyInfrastructureServiceCollectionExtensions.AddMyCompanyInfrastructureServices(services);
    // then the host's own services (if any)
    services.AddMyCompanyApiServices();
    return services;
}
```

## Why per-project methods register only their own services

If every project chained its children, diamond dependency graphs
(`Host → A → Shared`, `Host → B → Shared`) would register `Shared` twice.
Because each generated method registers only its own assembly and the aggregator invokes each
module **once** from the flattened reference closure, duplicate registration cannot happen.

## Naming rules

`AssemblyName` is sanitized to a PascalCase identifier: split on non-alphanumeric characters,
uppercase each segment's first letter, join. Examples:

| AssemblyName | Method |
|---|---|
| `MyCompany.Infrastructure` | `AddMyCompanyInfrastructureServices` |
| `my-lib` | `AddMyLibServices` |
| `sample_host` | `AddSampleHostServices` |

> ⚠️ Two assemblies that sanitize to the same identifier (e.g. `My.Lib` and `My_Lib`) would
> produce colliding extension class names. Rename one assembly if you hit this.

## Required Scope Validation across projects

`[assembly: RequiredExternalScope(typeof(T), DiServiceScope)]` uses the same reachability as the
module markers above: the generator scans the current assembly's own attributes plus every
referenced assembly's (directly or transitively), so a lock declared in one project is honored by
a `[Service<T>]` or explicit lifetime attribute in any other project that references it —
including the project that owns `T` itself needing no dependency on whatever library `T` came from.

If two reachable assemblies lock the same type to different lifetimes, that's `DIGEN010`, not a
silent pick — keep exactly one `[assembly: RequiredExternalScope]` declaration per external type
across the whole solution. A `[RequiredScope]` declared directly on `T` always takes precedence
over any `[assembly: RequiredExternalScope]` for the same type.

## Checklist

- Install the package (or analyzer `ProjectReference`) in **every** project that declares
  services or uses `[Inject]` — analyzer references do not flow transitively.
- The host calls `Add{Host}AllServices()` once; nothing else is needed.
- Libraries that should *not* auto-register anything simply don't use the attributes.
