using System;
using DIGen;
using Sample.Domain;

// [assembly: RequiredExternalScope] locks the lifetime for IOrderRepository from Sample.Domain.
// Use [Service<IOrderRepository>] on the implementation to auto-resolve the locked scope.
[assembly: RequiredExternalScope(typeof(Sample.Domain.IOrderRepository), DiServiceScope.Scoped)]

namespace Sample.Infrastructure;

/// <summary>
/// [Service&lt;T&gt;] resolves lifetime automatically from the [RequiredExternalScope] declaration above.
/// </summary>
[Service<IOrderRepository>]
public class ExternalScopedOrderRepository : IOrderRepository
{
    public IReadOnlyList<string> GetOrders() => ["external-order-1", "external-order-2"];
}
