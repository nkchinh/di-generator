using DIGen;
using Sample.Domain;

namespace Sample.Host;

public interface IOrderProcessor
{
    string ProcessAll();
}

/// <summary>
/// Demonstrates [Inject]: the generator creates one constructor assigning both members,
/// decorated with [ActivatorUtilitiesConstructor].
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
