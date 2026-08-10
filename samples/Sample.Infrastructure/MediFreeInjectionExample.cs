using DIGen;
using Sample.Domain;

namespace Sample.Infrastructure;

public interface IMediFreeInjectionReport
{
    string Describe();
}

/// <summary>
/// Demonstrates that a project with no MEDI reference can publish complete [Inject] metadata.
/// Sample.Host reads that metadata and generates the factory delegate against MEDI on its behalf.
/// </summary>
[ScopedService<IMediFreeInjectionReport>]
public partial class MediFreeInjectionReport : IMediFreeInjectionReport
{
    // Required: resolved by type from another service published by this MEDI-free project.
    [Inject] private readonly IOrderRepository _repository;

    // Keyed: Sample.Host generates GetRequiredKeyedService(..., "email") from the published key.
    [Inject("email")] private readonly INotifier _notifier;

    // Optional: this nullable type is deliberately not registered, so the host factory uses
    // GetOptional<T>() and the generated constructor receives null instead of throwing.
    [Inject] private readonly IInfrastructureMetadata? _metadata = null;

    public string Describe()
    {
        var metadata = _metadata is null ? "optional=missing" : $"optional={_metadata.Name}";
        return $"required={_repository.GetOrders().Count}; keyed={_notifier.Channel}; {metadata}";
    }
}

/// <summary>Deliberately not registered, to demonstrate nullable optional metadata.</summary>
public interface IInfrastructureMetadata
{
    string Name { get; }
}
