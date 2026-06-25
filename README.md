# AutoValidate.Generator

[![NuGet](https://img.shields.io/nuget/v/AutoValidate.Generator.svg)](https://www.nuget.org/packages/AutoValidate.Generator/)
[![CI](https://github.com/Swevo/AutoValidate.Generator/actions/workflows/build.yml/badge.svg)](https://github.com/Swevo/AutoValidate.Generator/actions)

**Compile-time FluentValidation wiring for .NET.**

AutoValidate.Generator uses Roslyn source generators to automatically discover your `AbstractValidator<T>` subclasses and generate `AddValidators()` on `IServiceCollection` — no assembly scanning, no reflection, no runtime overhead.

---

## Installation

```bash
dotnet add package AutoValidate.Generator
dotnet add package FluentValidation
```

---

## Quick Start

Define your validators as normal:

```csharp
public class Order
{
    public string CustomerName { get; set; } = "";
    public decimal Total { get; set; }
}

public class OrderValidator : AbstractValidator<Order>
{
    public OrderValidator()
    {
        RuleFor(x => x.CustomerName).NotEmpty();
        RuleFor(x => x.Total).GreaterThan(0);
    }
}
```

Register everything in one line — no manual wiring:

```csharp
builder.Services.AddValidators();
```

AutoValidate discovers every non-abstract `AbstractValidator<T>` in your assembly at **compile time** and generates the registration code for you.

---

## Attributes

### `[SkipValidator]`

Exclude a validator from auto-registration (e.g. test validators, base classes you register manually):

```csharp
[SkipValidator]
public class TestOrderValidator : AbstractValidator<Order> { }
```

### `[ValidatorLifetime]`

Override the DI lifetime. Default is `Scoped`.

```csharp
using AutoValidate;

// Singleton — validator has no mutable state
[ValidatorLifetime(ValidatorLifetime.Singleton)]
public class ConfigValidator : AbstractValidator<AppConfig> { }

// Transient — validator has per-request dependencies
[ValidatorLifetime(ValidatorLifetime.Transient)]
public class RequestValidator : AbstractValidator<CreateOrderRequest> { }
```

Available lifetimes: `ValidatorLifetime.Scoped` (default), `ValidatorLifetime.Singleton`, `ValidatorLifetime.Transient`.

### `[ValidateOnStartup]`

Register a hosted service that validates an instance of the model when the application starts. Useful for validating configuration objects.

```csharp
[ValidateOnStartup]
public class AppSettingsValidator : AbstractValidator<AppSettings>
{
    public AppSettingsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
        RuleFor(x => x.ApiKey).MinimumLength(32);
    }
}
```

`AppSettings` must be registered in DI (e.g. via `services.AddSingleton(appSettings)`). If the instance is not found, startup validation is silently skipped.

If validation fails at startup, an `InvalidOperationException` is thrown — your app will not start.

---

## Minimal API Integration

`WithValidation<T>()` attaches a validation endpoint filter that automatically returns `400 ValidationProblem` for invalid requests:

```csharp
app.MapPost("/orders", (Order order) => Results.Ok())
   .WithValidation<Order>();
```

The generated `ValidationFilter<T>` resolves `IValidator<T>` from DI, validates the first matching argument, and returns RFC 7807-compliant validation errors.

Requires .NET 7 or later.

---

## How It Works

At build time, the generator:

1. Scans all type declarations with a base list
2. Walks the inheritance chain looking for `FluentValidation.AbstractValidator<T>`
3. Skips abstract classes and `[SkipValidator]`-decorated types
4. Emits `AddValidators()` with the correct `AddScoped` / `AddSingleton` / `AddTransient` calls
5. Emits `ValidationFilter<T>` and `ValidatorStartupService<T>` helpers as needed

No reflection. No assembly scanning. No runtime cost.

---

## Diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| AV001 | Warning | Multiple validators found for the same model type. Only the first is registered. |
| AV002 | Warning | AutoValidate attribute on a class that does not inherit `AbstractValidator<T>`. |

---

## Comparison with Assembly Scanning

| Feature | `AddValidatorsFromAssembly()` | AutoValidate.Generator |
|---------|-------------------------------|------------------------|
| Discovery | Runtime reflection | Compile-time |
| Registration overhead | Assembly scan on startup | Zero |
| AOT / NativeAOT compatible | ❌ | ✅ |
| IDE navigation | ❌ | ✅ (generated code is inspectable) |
| Startup validation | Manual | `[ValidateOnStartup]` |

---

## Also by the same author

| Package | Description |
|---|---|
| [**AutoWire**](https://github.com/Swevo/AutoWire) | Compile-time DI auto-registration — `[Scoped]`/`[Singleton]`/`[Transient]` generates `IServiceCollection` code. Zero reflection. |
| [**AutoMap.Generator**](https://github.com/Swevo/AutoMap.Generator) | Compile-time object mapping — `[Map(typeof(Dto))]` generates `ToDto()` extension methods. Zero reflection, AOT-safe. |
| [**AutoResult.Generator**](https://github.com/Swevo/AutoResult.Generator) | Compile-time `Result<T>` monad — `[TryWrap]` generates `Try*()` wrappers for sync, async and void methods. |
| [**AutoQuery.Generator**](https://github.com/Swevo/AutoQuery.Generator) | Compile-time LINQ query specs — `[QuerySpec(typeof(T))]` generates `Apply(IQueryable<T>)`. |
| [**AutoDispatch.Generator**](https://github.com/Swevo/AutoDispatch.Generator) | Compile-time CQRS dispatcher — `[Handler]` generates a strongly-typed `IDispatcher`. No `IRequest<T>`, no reflection. |

---

## License

MIT © Justin Bannister
