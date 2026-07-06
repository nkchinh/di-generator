using Microsoft.Extensions.DependencyInjection;
using Sample.Host;
using Sample.Infrastructure;

// AddSampleHostAllServices() is generated: it chains AddSampleDomainServices() and
// AddSampleInfrastructureServices() from the referenced projects, then the host's own services.
var services = new ServiceCollection().AddSampleHostAllServices();
await using var provider = services.BuildServiceProvider();

using var scope = provider.CreateScope();

var processor = scope.ServiceProvider.GetRequiredService<IOrderProcessor>();
Console.WriteLine(processor.ProcessAll());

var email = provider.GetRequiredKeyedService<INotifier>("email");
var sms = provider.GetRequiredKeyedService<INotifier>("sms");
Console.WriteLine($"Keyed notifiers resolved: {email.Channel}, {sms.Channel}");
