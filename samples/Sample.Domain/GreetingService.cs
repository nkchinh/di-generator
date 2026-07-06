using DIGen;

namespace Sample.Domain;

public interface IGreetingService
{
    string Greet(string name);
}

[SingletonService<IGreetingService>]
public class GreetingService : IGreetingService
{
    public string Greet(string name) => $"Hello, {name}!";
}

[RequiredScope(DiServiceScope.Scoped)]
public interface IOrderRepository
{
    IReadOnlyList<string> GetOrders();
}
