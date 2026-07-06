using DIGen;
using Sample.Domain;

namespace Sample.Infrastructure;

// Lifetime resolved automatically from IOrderRepository's [RequiredScope] in Sample.Domain —
// Sample.Infrastructure never has to spell out (or accidentally get wrong) the lifetime.
[Service<IOrderRepository>]
public class InMemoryOrderRepository : IOrderRepository
{
    public IReadOnlyList<string> GetOrders() => ["order-1", "order-2"];
}

public interface INotifier
{
    string Channel { get; }
}

[SingletonService<INotifier>("email")]
public class EmailNotifier : INotifier
{
    public string Channel => "email";
}

[SingletonService<INotifier>("sms")]
public class SmsNotifier : INotifier
{
    public string Channel => "sms";
}
