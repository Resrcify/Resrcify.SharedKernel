# Resrcify.SharedKernel.DomainDrivenDesign

`Resrcify.SharedKernel.DomainDrivenDesign` provides reusable DDD primitives and abstractions for entities, value objects, domain events, and aggregate roots.

## Table of Contents

- [Resrcify.SharedKernel.DomainDrivenDesign](#resrcifysharedkerneldomaindrivendesign)
  - [Table of Contents](#table-of-contents)
  - [What you get](#what-you-get)
  - [Prerequisites](#prerequisites)
  - [Install](#install)
    - [Option A: Project reference](#option-a-project-reference)
    - [Option B: NuGet package](#option-b-nuget-package)
  - [Core abstractions](#core-abstractions)
  - [Usage guide](#usage-guide)
    - [Aggregate root example](#aggregate-root-example)
    - [Domain event example](#domain-event-example)
    - [Value object example](#value-object-example)
  - [Related modules](#related-modules)

## What you get

- Interfaces from `Resrcify.SharedKernel.Abstractions.DomainDrivenDesign`:
    - `IAggregateRoot<TId>`
    - `IAuditableEntity`
    - `IDeletableEntity`
    - `IDomainEvent`
- Primitives in `Primitives/`:
    - `AggregateRoot<TId>`
    - `Entity<TId>`
    - `DomainEvent`
    - `Enumeration<TEnum>`
    - `ValueObject`

## Prerequisites

- .NET 10 SDK.

## Install

### Option A: Project reference

```xml
<ProjectReference Include="..\path\to\Resrcify.SharedKernel.DomainDrivenDesign.csproj" />
```

### Option B: NuGet package

```xml
<PackageReference Include="Resrcify.SharedKernel.DomainDrivenDesign" Version="<latest>" />
```

CLI:

```powershell
dotnet add package Resrcify.SharedKernel.DomainDrivenDesign
```

## Core abstractions

- **Entity identity and equality** via `Entity<TId>`.
- **Aggregate consistency boundary** via `AggregateRoot<TId>`.
- **Event capture inside aggregates** via `RaiseDomainEvent(...)` and `GetDomainEvents()`.
- **Immutable value semantics** via `ValueObject`.
- **Smart-enum style values** via `Enumeration<TEnum>`.

## Usage guide

### Aggregate root example

```csharp
public sealed class Order
    : AggregateRoot<int>
{
    private readonly List<OrderItem> _items = [];

    public string CustomerId { get; private set; }

    public Order(
        int id,
        string customerId)
        : base(id)
    {
        CustomerId = customerId;
    }

    public void AddItem(
        string productId,
        int quantity)
    {
        var item = new OrderItem(productId, quantity);
        _items.Add(item);
        RaiseDomainEvent(new OrderItemAddedEvent(Id, item));
    }
}
```

### Domain event example

```csharp
public sealed class OrderItemAddedEvent
    : DomainEvent
{
    public int OrderId { get; }
    public OrderItem Item { get; }

    public OrderItemAddedEvent(
        int orderId,
        OrderItem item)
        : base(Guid.NewGuid())
    {
        OrderId = orderId;
        Item = item;
    }
}
```

### Value object example

```csharp
public sealed class Money
    : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(
        decimal amount,
        string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public override IEnumerable<object> GetAtomicValues()
    {
        yield return Amount;
        yield return Currency;
    }
}
```

## Related modules

- `Resrcify.SharedKernel.UnitOfWork` consumes domain-event abstractions in outbox interceptors.
- `Resrcify.SharedKernel.Repository` uses aggregate-root constraints in repository contracts.
- `samples/Resrcify.SharedKernel.WebApiExample` demonstrates domain layer usage.