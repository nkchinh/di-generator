using DIGen;

namespace Sample.Host;

public interface IReportService
{
    string Build();
}

/// <summary>
/// Demos two newer features:
///  1. Factory-delegate activation — because this class declares a user constructor in addition to
///     its [Inject] members, the generator registers it with a factory delegate that always calls
///     the generated [Inject] constructor (instead of relying on the container to pick a ctor).
///  2. Optional [Inject] — the nullable IPluginMetadata member resolves to null when the service is
///     not registered, instead of throwing at runtime.
/// </summary>
[ScopedService<IReportService>]
public partial class ReportService : IReportService
{
    // Required injection — IOrderProcessor is registered in this assembly (OrderProcessor), so no
    // DIGEN011 "unresolvable" warning is raised for it.
    [Inject] private readonly IOrderProcessor _processor;

    // Optional injection — nullable annotation; resolves to null when the service isn't registered.
    // IPluginMetadata is deliberately NOT registered, so this always comes back null here.
    [Inject] private readonly IPluginMetadata? _plugin;

    // Assigned from the user constructor below; the default keeps it non-null whichever ctor runs.
    private readonly string _locale = "en-US";

    // A user-defined constructor. Its presence switches the registration to a factory delegate, so
    // the generated [Inject] constructor above is the one the container activates.
    public ReportService(string locale)
    {
        _locale = locale;
        _processor = null!; // the factory-delegate path injects _processor via the generated ctor
    }

    public string Build()
    {
        var plugin = _plugin is null ? "(no plugin registered)" : _plugin.Name;
        return $"[{_locale}] plugin={plugin} :: {_processor.ProcessAll()}";
    }
}

/// <summary>
/// Deliberately never registered as a service, to demonstrate optional [Inject] resolution.
/// </summary>
public interface IPluginMetadata
{
    string Name { get; }
}
