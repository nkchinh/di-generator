# NkChinh.DI.Generator

> English documentation: [README.md](README.md)

**Source generator thuần** (pure generator) cho `Microsoft.Extensions.DependencyInjection`:
đăng ký service bằng attribute, sinh constructor từ `[Inject]`, và tự động nối chuỗi đăng ký
trong kiến trúc multi-project — **không thêm bất kỳ dependency runtime nào**. Toàn bộ code cần
thiết được sinh vào project của bạn dưới dạng `internal` lúc compile.

## Tính năng

- 🏷️ **Đăng ký bằng attribute** — `[SingletonService]`, `[ScopedService<T>]`, `[TransientService]`; hỗ trợ keyed service qua tham số key; tự động dùng `AddHostedService` cho class implement `IHostedService`.
- 🔧 **Sinh constructor từ `[Inject]`** — gắn lên field/property; mọi member `[Inject]` của một partial class được gom vào **duy nhất một** constructor, tham số đặt tên camelCase. Key tùy chọn (`[Inject("key")]`) yêu cầu dependency keyed; member nullable (`T?`) trở thành **optional** và nhận `null` khi service chưa đăng ký. Ở project tắt nullable, initializer được dùng làm tín hiệu optional tương đương. Generator sinh **factory delegate** khi cần chọn constructor, lookup keyed hoặc lookup optional — không cần `[ActivatorUtilitiesConstructor]`, không cần MEDI để compile class sở hữu.
- 🔑 **Hỗ trợ `[Inject("key")]`** — class đã đăng ký service luôn được sinh factory dùng `GetRequiredKeyedService`/`GetKeyedService`, kể cả khi không có constructor người dùng.
- 🛡️ **Lưới an toàn lúc biên dịch** — `DIGEN011` cảnh báo khi một `[Inject]` non-optional có type không được đăng ký trong assembly hiện tại hoặc trong `ServiceDefinition` do assembly được tham chiếu publish (chỉ luồng factory delegate); đăng ký ở assembly được tham chiếu vẫn resolve được lúc chạy và *không* bị báo.
- 🧩 **Multi-project** — project nào có service đều publish attribute `[assembly: ServiceDefinition]`. Project có MEDI sinh `Add{TênAssembly}OwnedServices()` để đăng ký service của chính assembly đó, kể cả type `internal`; method `Add{TênAssembly}Services()` là entry point gốc, compose các module MEDI và đăng ký trực tiếp service từ project không có MEDI đúng một lần (an toàn với diamond dependency).
- 🧬 **Chạy được ở project hoàn toàn không tham chiếu MEDI** — project Domain/Application chỉ khai báo interface và tự đăng ký qua `[Service<T>]`/attribute lifetime vẫn compile sạch dù không có bất kỳ dependency nào tới `Microsoft.Extensions.DependencyInjection`; các method dựa trên `IServiceCollection` chỉ xuất hiện ở project nào thực sự tham chiếu MEDI.
- 🔒 **Required Scope Validation** — khóa lifetime của một interface đúng một lần bằng `[RequiredScope]` (hoặc `[assembly: RequiredExternalScope]` cho type bên thứ ba); `[Service<T>]` tự động suy ra lifetime đã khóa, còn attribute lifetime tường minh nào trái với khóa sẽ là lỗi biên dịch — hết lo lỗi captive dependency (vd `DbContext` Scoped bị đăng ký nhầm thành Singleton).
- 🚨 **Diagnostic chuẩn compiler** — dùng sai là báo lỗi biên dịch (`DIGEN001`–`DIGEN010`); cấu trúc hợp lệ nhưng có rủi ro hiện thành cảnh báo (`DIGEN011`–`DIGEN013`), không bao giờ sinh code sai trong im lặng.
- 📦 **Gói NuGet thuần analyzer** — chỉ chứa assembly analyzer, không có `lib/`, không thêm dependency runtime nào.
- 🌱 **Thân thiện với trimming & Native AOT** — việc đăng ký service là các lệnh gọi `services.Add{Lifetime}<...>()` thuần được sinh sẵn lúc biên dịch, không dùng reflection để quét assembly lúc chạy, nên không có gì để trimmer phá vỡ và không có gì xung đột với Native AOT.

## Cài đặt

```xml
<PackageReference Include="NkChinh.DI.Generator" Version="0.0.4" PrivateAssets="all" />
```

Yêu cầu: .NET SDK 8+ (hỗ trợ project net8.0 và net10.0), C# 11+ cho generic attribute.
Chỉ project nào thực sự gọi `IServiceCollection` mới cần tham chiếu
`Microsoft.Extensions.DependencyInjection.Abstractions` ≥ 8.0 — project Domain/Application không
tham chiếu MEDI vẫn compile sạch (xem [README.md](README.md#how-it-works)).

## Dùng nhanh

```csharp
using DIGen;

[ScopedService<IOrderRepository>]           // AddScoped<IOrderRepository, OrderRepository>()
public class OrderRepository : IOrderRepository { }

[SingletonService]                          // AddSingleton<MemoryCache>()
public class MemoryCache { }

[SingletonService<IPaymentGateway>("stripe")] // AddKeyedSingleton(..., "stripe")
public class StripeGateway : IPaymentGateway { }
```

```csharp
// AssemblyName "MyCompany.Api" → AddMyCompanyApiServices()
builder.Services.AddMyCompanyApiServices();
```

### Constructor injection với `[Inject]`

```csharp
[TransientService<IOrderProcessor>]
public partial class OrderProcessor : IOrderProcessor
{
    [Inject] private readonly IOrderRepository _repository; // → tham số orderRepository
    [Inject] private readonly IPaymentGateway _gateway;     // → tham số paymentGateway
}
```

Generator sinh **một** constructor:

```csharp
public OrderProcessor(IOrderRepository orderRepository, IPaymentGateway paymentGateway)
{
    this._repository = orderRepository;
    this._gateway = paymentGateway;
}
```

Tên tham số suy ra từ **tên kiểu** (`IOrderRepository` → `orderRepository`); trùng kiểu thì
fallback về tên member; keyword C# được xử lý tự động.

#### Constructor do người dùng khai báo → factory delegate

Khi một class có `[Inject]` đồng thời khai báo constructor của người dùng, container có thể chọn
constructor của người dùng thay vì constructor đã sinh — để lại field trống. Generator tránh điều
này bằng cách sinh **factory delegate** thay vì `ServiceDescriptor(Type, Type, ServiceLifetime)`
thông thường, nên constructor `[Inject]` đã sinh luôn được gọi:

```csharp
[ScopedService<IReportService>]
public partial class ReportService : IReportService
{
    [Inject] private readonly IOrderRepository _repository;

    // Sự hiện diện của ctor người dùng bật chế độ factory-delegate:
    public ReportService(IReportOptions options) { /* ... */ }
}
// → registrations.Add((..., sp => new ReportService(
//       InjectServiceResolver.GetRequired<IOrderRepository>(sp))));
```

Delegate chỉ dùng `System.IServiceProvider` (thuộc BCL) và helper `InjectServiceResolver` luôn được
nhúng — đều thuần BCL — nên factory compile và chạy được kể cả ở project Domain không tham chiếu
MEDI.

#### `[Inject]` optional

Trong project bật nullable, member `[Inject]` chỉ optional khi khai `T?`. Ở project tắt nullable,
initializer được dùng làm tín hiệu optional vì không thể biểu diễn contract bằng `T?`. Member optional
resolve qua `IServiceProvider.GetService` (trả `null` khi thiếu); member required dùng
`GetRequired<T>` và ném lỗi nếu service chưa đăng ký:

```csharp
[Inject] private readonly IOrderRepository _repository;           // required — lỗi nếu thiếu
[Inject] private readonly ITelemetryInitializer? _telemetry;      // optional — null nếu thiếu
```

Member non-optional có type generator không thấy được đăng ký trong **assembly hiện tại hoặc trong
các `ServiceDefinition` được publish bởi assembly được tham chiếu** được báo **`DIGEN011`** — đăng ký ở
assembly được tham chiếu vẫn resolve được lúc chạy và *không* bị báo.

#### `[Inject]` keyed — resolve keyed khi ở luồng factory delegate

```csharp
[Inject("primary")] private readonly ICache _primaryCache;
```

`InjectAttribute` nhận key tùy chọn (`[Inject("key")]`). Khi class chứa nó được đăng ký làm service,
factory sinh ra sẽ resolve member **theo key** qua `GetRequiredKeyedService`/`GetKeyedService`, kể cả
khi class không có constructor người dùng. Riêng việc có key không bao giờ sinh cảnh báo.

### Multi-project

Cài package vào **mọi** project có khai báo service. Project nào có service sẽ publish các attribute
`[assembly: ServiceDefinition]` (không cần tham chiếu MEDI). Project có MEDI sinh hai method: một method
`OwnedServices()` chỉ đăng ký service của chính assembly, và một method `Services()` làm entry point
gốc. Root method gọi `OwnedServices()` của các project MEDI được tham chiếu, rồi đăng ký trực tiếp
union service từ các project không có MEDI:

```csharp
builder.Services.AddMyCompanyApiServices(); // đăng ký toàn bộ graph đúng một lần
```

Nhờ registration được compile trong assembly sở hữu, service `internal` của project MEDI vẫn dùng được
mà không cần mở quyền truy cập cho Host. Chỉ gọi method `Services()` của project gốc; `OwnedServices()`
là method phục vụ compose nội bộ giữa các project.

Chi tiết: [docs/multi-project.md](docs/multi-project.md). Xem [samples](samples) cho solution
3 project chạy được ngay.

### Required Scope Validation

Khóa lifetime của một interface một lần, để không class nào lỡ đăng ký sai lifetime của nó nữa
(lỗi captive dependency kinh điển — repository Scoped bị đăng ký nhầm thành Singleton):

```csharp
using DIGen;

// Khóa IOrderRepository ở Scoped — khai báo một lần, ở bất cứ đâu interface này được định nghĩa.
[RequiredScope(DiServiceScope.Scoped)]
public interface IOrderRepository { /* ... */ }

// Tự suy ra lifetime từ khóa trên — không có lifetime nào để viết sai.
[Service<IOrderRepository>]
public class SqlOrderRepository : IOrderRepository { /* ... */ }

// Attribute tường minh mà trái khóa là lỗi biên dịch (DIGEN009):
[SingletonService<IOrderRepository>]   // lỗi: đã khóa Scoped, không phải Singleton
public class Wrong : IOrderRepository { /* ... */ }
```

Với type bạn không sở hữu (interface bên thứ ba, `DbContext`,
`StackExchange.Redis.IConnectionMultiplexer`, …), khóa nó ở bất kỳ project nào đã tham chiếu thư
viện đó — project sở hữu interface gốc không cần thêm dependency:

```csharp
// Ở project đã tham chiếu StackExchange.Redis:
[assembly: RequiredExternalScope(typeof(IConnectionMultiplexer), DiServiceScope.Singleton)]
```

`[RequiredScope]` gắn trực tiếp lên type luôn thắng nếu cả hai cùng tồn tại. Xem
[docs/diagnostics.md](docs/diagnostics.md) cho `DIGEN008`–`DIGEN010`.

## Diagnostics

Xem bảng đầy đủ tại [docs/diagnostics.md](docs/diagnostics.md) hoặc [README.md](README.md#diagnostics).
Tóm tắt: `DIGEN001`–`DIGEN010` là lỗi biên dịch (dùng sai attribute, disagree scope, …); `DIGEN011`
–`DIGEN013` là cảnh báo cho các trường hợp có thể hợp lệ nhưng cần chú ý, gồm `[Inject]` không
optional không resolve được và service tham chiếu không accessible.

## Giấy phép

[MIT](LICENSE) © NkChinh
