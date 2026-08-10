using DIGen;
using Sample.Domain;

namespace Sample.Host;

public interface IOrderProcessor
{
    string ProcessAll();
}

/// <summary>
/// Demonstrates required [Inject]: the generator creates one constructor assigning both members.
/// Because this class has no user constructor, MEDI activates that generated constructor directly.
/// </summary>
[TransientService<IOrderProcessor>]
public partial class OrderProcessor : IOrderProcessor
{
    [Inject] private readonly IOrderRepository _repository;
    [Inject] private readonly IGreetingService _greetingService;

    public string ProcessAll()
    {
        var orders = string.Join(", ", _repository.GetOrders());
        return $"{_greetingService.Greet("operator")} Processing: {orders}";
    }
}
