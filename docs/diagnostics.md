# Diagnostics reference

All diagnostics use category `NkChinh.DI.Generator`.

## DIGEN001

**Service type not implemented** — Error

A class is annotated with a generic lifetime attribute (`[SingletonService<TService>]`,
`[ScopedService<TService>]`, `[TransientService<TService>]`) but does not implement or inherit
`TService`.

```csharp
public interface IOrderRepository { }

[SingletonService<IOrderRepository>]  // DIGEN001: Unrelated does not implement IOrderRepository
public class Unrelated { }
```

**Fix:** implement/inherit the service type, or change the generic argument.

## DIGEN002

**[Inject] containing type must be partial** — Error

The generated constructor lives in a separate partial declaration, so the class — and every
containing (outer) type — must be declared `partial`.

```csharp
public class OrderService            // DIGEN002: add 'partial'
{
    [Inject] private readonly IOrderRepository _repository;
}
```

## DIGEN003

**[Inject] member must be an instance member** — Error

`static` and `const` members cannot be assigned from a constructor parameter.

## DIGEN004

**[Inject] property cannot be assigned from a constructor** — Error

The property has no setter and is not an auto-property. Supported shapes:

```csharp
[Inject] public IClock Clock { get; }          // OK: get-only auto-property
[Inject] public IClock Clock { get; set; }     // OK
[Inject] public IClock Clock { get; init; }    // OK
[Inject] public IClock Clock => _clock;        // DIGEN004: expression-bodied
```

## DIGEN005

**Abstract class cannot be registered as a service** — Warning

The DI container cannot instantiate an abstract class; the attribute is ignored. Move the
attribute to the concrete subclasses.

## DIGEN006

**Multiple service lifetime attributes** — Error

A class may carry exactly one lifetime attribute. Combining `[SingletonService]` with
`[ScopedService]` (or a generic with a non-generic variant) is ambiguous.

## DIGEN007

**[Inject] is only supported inside classes** — Error

`[Inject]` members inside structs, record structs, or interfaces are not supported: the DI
container activates classes, and struct constructor semantics differ.

## DIGEN008

**[Service<T>] used but T has no locked scope** — Error

`[Service<TService>]` registers a class using whatever lifetime `TService` is locked to via
`[RequiredScope]` (on the interface) or `[assembly: RequiredExternalScope]` (for a type the
project doesn't own). If neither lock exists, there's nothing to resolve the lifetime from.

```csharp
public interface IOrderRepository { }   // no [RequiredScope]

[Service<IOrderRepository>]             // DIGEN008: IOrderRepository has no locked scope
public class OrderRepository : IOrderRepository { }
```

**Fix:** add `[RequiredScope(DiServiceScope.Scoped)]` to `IOrderRepository` (or an
`[assembly: RequiredExternalScope(typeof(IOrderRepository), DiServiceScope.Scoped)]` if you
don't own the interface), or use an explicit `[SingletonService<T>]` / `[ScopedService<T>]` /
`[TransientService<T>]` instead.

## DIGEN009

**Lifetime attribute disagrees with the locked scope** — Error

`TService` is locked to a lifetime (via `[RequiredScope]` or `[assembly: RequiredExternalScope]`),
but the registration attribute specifies a different one.

```csharp
[RequiredScope(DiServiceScope.Scoped)]
public interface IOrderRepository { }

[SingletonService<IOrderRepository>]    // DIGEN009: locked to Scoped, not Singleton
public class OrderRepository : IOrderRepository { }
```

**Fix:** match the attribute's lifetime to the lock, or use `[Service<T>]` to have it resolved
automatically.

## DIGEN010

**Conflicting RequiredExternalScope declarations** — Error

Two `[assembly: RequiredExternalScope]` declarations, both reachable from the current project
(directly or through project references), lock the same type to different lifetimes.

**Fix:** keep exactly one `[assembly: RequiredExternalScope]` declaration per external type across
the whole solution.

## DIGEN011

**[Inject] constructor parameter may not be resolvable from DI** — Warning

A class with `[Inject]` members and a user-defined constructor is activated through a generated
**factory delegate** (rather than the default `ServiceDescriptor(Type, Type, ServiceLifetime)`).
That delegate resolves each `[Inject]` parameter at runtime from the `IServiceProvider`. When a
non-optional `[Inject]` member has a type the generator can't see registered as a service in the
*current assembly*, it warns — if the service isn't registered at runtime either, the factory will
throw `InvalidOperationException` at resolution time.

> The check is intentionally scoped to the current assembly to avoid false positives from services
> registered in *referenced* assemblies. A member whose type is registered in another project (for
> example a Domain service consumed by a Host-located class) is resolvable at runtime and is not
> reported.

```csharp
public interface IMissingService { }   // not registered anywhere

[TransientService<IOrderProcessor>]
public partial class OrderProcessor : IOrderProcessor
{
    [Inject] private readonly IOrderRepository _repository; // OK: registered in a referenced assembly
    [Inject] private readonly IMissingService _missing;      // DIGEN011: not registered here

    // A user-defined constructor is what activates the factory-delegate path:
    public OrderProcessor(IConfiguration config) { /* ... */ }
}
```

**Fix (one of):**

- Register the missing type as a service (`[SingletonService<...>]` / `[ScopedService<...>]` /
  `[TransientService<...>]`) somewhere in the same assembly, or
- Make the dependency **optional** by annotating the member as nullable (`T?`) or giving it a
  default value (`= null` / `= default`). Optional members resolve via
  `IServiceProvider.GetService` and tolerate a missing registration:

  ```csharp
  [Inject] private readonly IMissingService? _missing;          // optional
  [Inject] private readonly ILogger _logger = NullLogger.Instance; // optional (default value)
  ```

- Or move the registration of the member's type into the current assembly so the generator can see
  it (cross-assembly registrations are resolvable at runtime but invisible to this check).

This diagnostic only fires on the **factory-delegate path** (class has `[Inject]` members **and** a
user-defined constructor **and** the class itself is registered as a service). Classes with only
`[Inject]` members and no user constructor use the standard `ServiceDescriptor` and are activated by
the container's own constructor-selection logic, so no warning applies.

## DIGEN012

**[Inject] keyed service requires MEDI reference** — Warning

`[Inject("key")]` accepts an optional service key. Keyed-service resolution in .NET DI requires
`IKeyedServiceProvider`, which is part of `Microsoft.Extensions.DependencyInjection` (MEDI) — a type
that may not exist in the current project at all. Today the generator does **not** emit keyed lookup
for `[Inject]` members; the key is used as a compile-time signal, and when the current project has
**no reference to MEDI**, the generator reports `DIGEN012` to make sure the author knows the key will
be ignored and the member resolved by type (`sp.GetService(typeof(T))`) at runtime.

```csharp
// Project has no PackageReference to Microsoft.Extensions.DependencyInjection.*

public partial class GreetingService
{
    // DIGEN012: key 'primary' is ignored — resolved without a key at runtime.
    [Inject("primary")] private readonly ILogger _logger;
}
```

**Fix (one of):**

- Remove the key from `[Inject]` if the key was only tentative, or
- Keep the key as documentation of intent (the warning is informational; the member still compiles
  and resolves by type), or
- If you need **real** keyed resolution at runtime, resolve the keyed service explicitly (via
  `IKeyedServiceProvider` / `[FromKeyedServices]`) rather than relying on `[Inject("key")]` for it,
  or
- Move the class into a project that references MEDI if you want the warning to disappear while
  keeping the key (the resolution behavior is the same either way).
