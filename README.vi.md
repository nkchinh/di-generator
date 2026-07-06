# NkChinh.DI.Generator

> English documentation: [README.md](README.md)

**Source generator thuần** (pure generator) cho `Microsoft.Extensions.DependencyInjection`:
đăng ký service bằng attribute, sinh constructor từ `[Inject]`, và tự động nối chuỗi đăng ký
trong kiến trúc multi-project — **không thêm bất kỳ dependency runtime nào**. Toàn bộ code cần
thiết được sinh vào project của bạn dưới dạng `internal` lúc compile.

## Tính năng

- 🏷️ **Đăng ký bằng attribute** — `[SingletonService]`, `[ScopedService<T>]`, `[TransientService]`; hỗ trợ keyed service qua tham số key; tự động dùng `AddHostedService` cho class implement `IHostedService`.
- 🔧 **Sinh constructor từ `[Inject]`** — gắn lên field/property; mọi member `[Inject]` của một partial class được gom vào **duy nhất một** constructor, tham số đặt tên camelCase, kèm `[ActivatorUtilitiesConstructor]`.
- 🧩 **Multi-project** — mỗi project sinh `Add{TênAssembly}Services()`; project host sinh thêm `Add{TênAssembly}AllServices()` nối chuỗi tất cả project được tham chiếu, mỗi module đúng một lần (an toàn với diamond dependency).
- 🧬 **Chạy được ở project hoàn toàn không tham chiếu MEDI** — project Domain/Application chỉ khai báo interface và tự đăng ký qua `[Service<T>]`/attribute lifetime vẫn compile sạch dù không có bất kỳ dependency nào tới `Microsoft.Extensions.DependencyInjection`; các method dựa trên `IServiceCollection` chỉ xuất hiện ở project nào thực sự tham chiếu MEDI.
- 🔒 **Required Scope Validation** — khóa lifetime của một interface đúng một lần bằng `[RequiredScope]` (hoặc `[assembly: RequiredExternalScope]` cho type bên thứ ba); `[Service<T>]` tự động suy ra lifetime đã khóa, còn attribute lifetime tường minh nào trái với khóa sẽ là lỗi biên dịch — hết lo lỗi captive dependency (vd `DbContext` Scoped bị đăng ký nhầm thành Singleton).
- 🚨 **Diagnostic chuẩn compiler** — dùng sai là báo lỗi biên dịch (`DIGEN001`–`DIGEN010`).
- 📦 **Gói NuGet thuần analyzer** — chỉ chứa assembly analyzer, không có `lib/`, không thêm dependency runtime nào.
- 🌱 **Thân thiện với trimming & Native AOT** — việc đăng ký service là các lệnh gọi `services.Add{Lifetime}<...>()` thuần được sinh sẵn lúc biên dịch, không dùng reflection để quét assembly lúc chạy, nên không có gì để trimmer phá vỡ và không có gì xung đột với Native AOT.

## Cài đặt

```xml
<PackageReference Include="NkChinh.DI.Generator" Version="0.0.2" PrivateAssets="all" />
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

Tên tham số suy ra từ **tên kiểu** (`IOrderRepository` → `orderRepository`); trùng kiểu thì
fallback về tên member; keyword C# được xử lý tự động.

### Multi-project

Cài package vào **mọi** project có khai báo service. Host gọi một lệnh duy nhất:

```csharp
builder.Services.AddMyCompanyApiAllServices(); // chaining toàn bộ project con + host
```

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

## Giấy phép

[MIT](LICENSE) © NkChinh
