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
