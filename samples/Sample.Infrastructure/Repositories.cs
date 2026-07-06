using DIGen;
using Sample.Domain;

namespace Sample.Infrastructure;

[ScopedService<IOrderRepository>]
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
