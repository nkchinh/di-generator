# Multi-project architecture

## How it works

Every project that installs `NkChinh.DI.Generator` and declares at least one service:

1. **Publishes an assembly-level `ServiceDefinition` attribute per service** — always emitted,
   regardless of whether the project references Microsoft.Extensions.DependencyInjection (MEDI).
   The attribute carries everything a referencing host needs to register the service without
   re-deriving it: implementation type, service type, resolved lifetime, optional key, hosted-service
   flag, and the `[Inject]` member metadata (names/types/keys/optionality in generated-constructor
   order):

   ```csharp
   // AssemblyName = MyCompany.Infrastructure
   [assembly: DIGen.Generated.ServiceDefinition(
       typeof(OrderRepository),      // implementation type
       typeof(IOrderRepository),     // registered service type
       (int)DIGen.DiServiceScope.Scoped,
       null,                         // no service key
       false,                        // not an IHostedService
       false,                        // no generated factory required
       new string[] { },
       new System.Type[] { },
       new string[] { },
       new bool[] { })]
   ```

   The attribute is an internal infrastructure type embedded per project (like all DIGen
   attributes), but its parameters are all framework types (`System.Type`, `int`, `string`, `bool`,
   arrays thereof) — never another project-embedded type — so each assembly's published definitions
   are readable by any referencing compilation.

2. **Only if the project resolves MEDI**, it emits two extension methods:
   `Add{Assembly}OwnedServices(this IServiceCollection)` registers only services owned by that
   assembly, while `Add{Assembly}Services(this IServiceCollection)` is the root orchestration method:

   ```csharp
    public static IServiceCollection AddMyCompanyApiServices(this IServiceCollection services)
    {
        AddMyCompanyApiOwnedServices(services);
        MyCompanyInfrastructureServiceCollectionExtensions
            .AddMyCompanyInfrastructureOwnedServices(services);
        // MEDI-free definitions are emitted directly, once:
        services.AddScoped<global::MyCompany.Domain.IOrderRepository, global::MyCompany.Infrastructure.SqlOrderRepository>();
       return services;
   }
   ```

   A project with its own services but no MEDI reference publishes its definitions and emits nothing
   `IServiceCollection`-based — the actual registration happens in whichever referencing MEDI-having
   project consumes the definitions.

## Why root methods compose owned modules

The MEDI-free `ServiceDefinition` publication is the cross-project contract. A root method calls the
owned registration method of every reachable MEDI module. Those calls are compiled inside the owning
assembly, so a module may safely register `internal` services without granting the root assembly
access. The root directly emits definitions only from assemblies without MEDI.

Because the root collects MEDI-free definitions as one union, diamond dependency graphs (`Host → A →
Shared`, `Host → B → Shared`) register `Shared` once, not once through each module path. Call only the
root project's `Add{Assembly}Services()` method; `OwnedServices()` is generated for composition.

Dependency modules and MEDI-free definitions are registered before the root assembly's owned services.
This keeps the normal MEDI rule that a root registration is the final override for the same service.

## Naming rules

`AssemblyName` is sanitized to a PascalCase identifier: split on non-alphanumeric characters,
uppercase each segment's first letter, join. Examples:

| AssemblyName | Host method |
|---|---|
| `MyCompany.Infrastructure` | `AddMyCompanyInfrastructureServices` |
| `my-lib` | `AddMyLibServices` |
| `sample_host` | `AddSampleHostServices` |

> ⚠️ Two assemblies that sanitize to the same identifier (e.g. `My.Lib` and `My_Lib`) would
> produce colliding extension class names. Rename one assembly if you hit this.

## Required Scope Validation across projects

`[assembly: RequiredExternalScope(typeof(T), DiServiceScope)]` uses the same reachability as the
published-definition scan above: the generator reads the current assembly's own attributes plus
every referenced assembly's (directly or transitively), so a lock declared in one project is honored
by a `[Service<T>]` or explicit lifetime attribute in any other project that references it —
including the project that owns `T` itself needing no dependency on whatever library `T` came from.

If two reachable assemblies lock the same type to different lifetimes, that's `DIGEN010`, not a
silent pick — keep exactly one `[assembly: RequiredExternalScope]` declaration per external type
across the whole solution. A `[RequiredScope]` declared directly on `T` always takes precedence
over any `[assembly: RequiredExternalScope]` for the same type.

## Checklist

- Install the package (or analyzer `ProjectReference`) in **every** project that declares
  services or uses `[Inject]` — analyzer references do not flow transitively.
- The root calls `Add{Root}Services()` once; nothing else is needed. It composes every MEDI module's
  owned registrations and every MEDI-free referenced definition.
- Transitive project references are included. For example, with
  `Host (MEDI) -> Infrastructure (no MEDI) -> Domain (no MEDI)`, Host does not need a direct
  reference to Domain for Domain's published service definitions to be registered.
- Libraries that should *not* auto-register anything simply don't use the attributes.
