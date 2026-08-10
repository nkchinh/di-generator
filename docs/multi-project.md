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

2. **Only if the project resolves MEDI**, it emits a single
   `Add{Assembly}Services(this IServiceCollection)` extension method that registers **its own
   services together with every `ServiceDefinition` published by a reachable referenced assembly**,
   in one pass:

   ```csharp
   public static IServiceCollection AddMyCompanyApiServices(this IServiceCollection services)
   {
       // own services merged with definitions read from referenced assemblies,
       // sorted by implementation type, registered directly:
       services.AddScoped<global::MyCompany.Domain.IOrderRepository, global::MyCompany.Infrastructure.SqlOrderRepository>();
       services.AddSingleton<global::MyCompany.Api.AppState>();
       return services;
   }
   ```

   A project with its own services but no MEDI reference publishes its definitions and emits nothing
   `IServiceCollection`-based — the actual registration happens in whichever referencing MEDI-having
   project consumes the definitions.

## Why each host registers referenced services itself

The MEDI-free `ServiceDefinition` publication is the cross-project contract. A host that resolves
MEDI reads the definitions published by every assembly in its reference closure — directly or
transitively — and emits concrete `Add{...}` calls for them. Because the host collects the union of
all reachable definitions once and sorts by implementation type, diamond dependency graphs
(`Host → A → Shared`, `Host → B → Shared`, or `Host` referencing both) cannot register `Shared`
twice: each referenced assembly's definitions are read exactly once per host compilation, and
`Shared` needs no MEDI reference of its own to participate.

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
- The host calls `Add{Host}Services()` once; nothing else is needed. It registers services owned
  by the host **and** every referenced project.
- Transitive project references are included. For example, with
  `Host (MEDI) -> Infrastructure (no MEDI) -> Domain (no MEDI)`, Host does not need a direct
  reference to Domain for Domain's published service definitions to be registered.
- Libraries that should *not* auto-register anything simply don't use the attributes.
