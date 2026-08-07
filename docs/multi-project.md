# Multi-project architecture

## How it works

Every project that installs `NkChinh.DI.Generator` and declares at least one service gets:

1. **`Collect{Assembly}Services`**, always emitted regardless of whether the project references
   Microsoft.Extensions.DependencyInjection (MEDI) — registers **only that project's own services**,
   as plain data:

   ```csharp
   // AssemblyName = MyCompany.Infrastructure
   namespace Microsoft.Extensions.DependencyInjection
   {
       public static class MyCompanyInfrastructureServiceCollectionExtensions
       {
           public static void CollectMyCompanyInfrastructureServices(
               ICollection<(Type ServiceType, Type ImplementationType, int Lifetime, string? Key, bool IsHostedService, Func<IServiceProvider, object>? Factory)> registrations)
           { /* ... */ }
       }
   }
   ```

   The tuple uses only framework types (`System.Type`, `int`, `string`, `bool`,
   `Func<IServiceProvider, object>`) — never a project-embedded class or enum — specifically so it's
   safe to pass across project references: a type embedded independently per project (like every
   other DIGen attribute) would be a *different* type in each assembly and wouldn't compile as a
   shared method parameter. The 6th tuple element (`Factory`) carries a factory delegate for classes
   that have both `[Inject]` members and a user-defined constructor (see
   [SPEC § Factory-delegate activation](SPEC.md#factory-delegate-activation)); it is `null`
   otherwise. Because the delegate references only `System.IServiceProvider` (BCL, not MEDI) and
   the always-embedded `InjectServiceResolver` helper, the tuple stays callable across MEDI-free
   projects.

2. An assembly-level **module marker** (internal infrastructure attribute), naming the `Collect`
   method — so the marker itself needs no MEDI reference either:

   ```csharp
   [assembly: DIGen.Generated.ServiceRegistrationModuleAttribute(
       "CollectMyCompanyInfrastructureServices",
       "Microsoft.Extensions.DependencyInjection.MyCompanyInfrastructureServiceCollectionExtensions")]
   ```

3. **Only if the project resolves MEDI**, two more members: `Add{Assembly}Services(this IServiceCollection)`
   (builds a list, calls `Collect`, materializes) and, if it also has referenced modules, the
   **aggregator** `Add{Assembly}AllServices`:

    ```csharp
    public static IServiceCollection AddMyCompanyApiAllServices(this IServiceCollection services)
    {
        var registrations = new List<(Type, Type, int, string?, bool, Func<IServiceProvider, object>?)>();
        // every referenced module's Collect method, exactly once, deterministic order —
        // regardless of whether that module itself has a MEDI reference
        MyCompanyDomainServiceCollectionExtensions.CollectMyCompanyDomainServices(registrations);
        MyCompanyInfrastructureServiceCollectionExtensions.CollectMyCompanyInfrastructureServices(registrations);
        // then the host's own services (if any)
        CollectMyCompanyApiServices(registrations);
        // materialized exactly once, at the very end
        return services.MaterializeServices(registrations);
    }
    ```

## Why per-project methods register only their own services

If every project chained its children, diamond dependency graphs
(`Host → A → Shared`, `Host → B → Shared`) would register `Shared` twice.
Because each generated `Collect` method registers only its own assembly, and the aggregator
invokes each module's `Collect` **once** from the flattened reference closure before a single
materialization pass, duplicate registration cannot happen — including when `Shared` has no MEDI
reference of its own and is only reachable through `A` and `B`'s references to it.

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
