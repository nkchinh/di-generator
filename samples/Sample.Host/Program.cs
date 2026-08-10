using Microsoft.Extensions.DependencyInjection;
using Sample.Domain;
using Sample.Host;
using Sample.Infrastructure;

// AddSampleHostServices() is generated: it registers the service definitions published by the
// directly referenced Sample.Infrastructure project, the transitively referenced Sample.Domain
// project (Host -> Infrastructure -> Domain), and the host's own services.
var services = new ServiceCollection().AddSampleHostServices();
await using var provider = services.BuildServiceProvider();

using var scope = provider.CreateScope();

// Sample.Host has no direct ProjectReference to Sample.Domain. Resolving this proves that the host
// consumes ServiceDefinition metadata from a transitive MEDI-free project.
var greeting = provider.GetRequiredService<IGreetingService>();
Console.WriteLine($"Transitive Domain service: {greeting.Greet("bridge")}");

var processor = scope.ServiceProvider.GetRequiredService<IOrderProcessor>();
Console.WriteLine(processor.ProcessAll());

var email = provider.GetRequiredKeyedService<INotifier>("email");
var sms = provider.GetRequiredKeyedService<INotifier>("sms");
Console.WriteLine($"Keyed notifiers resolved: {email.Channel}, {sms.Channel}");

// This service is declared in Sample.Infrastructure, which has no MEDI reference. Its published
// metadata tells this host factory how to resolve required, keyed, and optional [Inject] members.
var mediFreeReport = scope.ServiceProvider.GetRequiredService<IMediFreeInjectionReport>();
Console.WriteLine($"MEDI-free inject metadata: {mediFreeReport.Describe()}");

// ReportService combines [Inject] members with a user-defined constructor, so the generator
// registers it with a factory delegate that always activates the generated [Inject] ctor.
// Its optional [Inject] IPluginMetadata? member resolves to null (not registered).
var report = scope.ServiceProvider.GetRequiredService<IReportService>();
Console.WriteLine(report.Build());
