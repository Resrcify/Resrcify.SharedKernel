# Resrcify.SharedKernel.DomainDrivenDesign

## Description
The **Resrcify.SharedKernel.DomainDrivenDesign** library has been developed to enhance the application of domain-driven design (DDD) principles within .NET projects. This repository provides essential abstractions and primitives for effectively modeling aggregates, entities, value objects, and domain events.

## Prerequisites
Before using **Resrcify.SharedKernel.DomainDrivenDesign**, ensure that your project meets the following requirements:

- .NET 8.0 is installed.
- Please note that all external package references in this repository are private, meaning that you are forced add them to your own project if you need/wish to use them. This is to maintain correct dependency references in accordance with Clean Architecture.

## Installation
To integrate **Resrcify.SharedKernel.DomainDrivenDesign** into your project, you can either clone the source code or install the NuGet package, depending on your preference.

### Download and reference the project files
1. Clone this repository
```bash
git clone https://github.com/Resrcify/Resrcify.SharedKernel.git
```
2. Add the **Resrcify.SharedKernel.DomainDrivenDesign** project to your solution/project.

- By referencing the project in your ``.csproj`` file
    ```xml
    <ProjectReference Include="../path/to/Resrcify.SharedKernel.DomainDrivenDesign.csproj" />
    ```
- Or by using the command line to reference the project
    ```bash
    dotnet add reference path/to/Resrcify.SharedKernel.DomainDrivenDesign.csproj
    ```

### Download and reference Nuget package
1. Add the package from NuGet:
- By referencing in your ``.csproj`` file
    ```xml
    <PackageReference Include="Resrcify.SharedKernel.DomainDrivenDesign" Version="1.8.5" />
    ```
- Or by using the command line
    ```bash
    dotnet add package Resrcify.SharedKernel.DomainDrivenDesign
    ```

## Configuration
No specific configuration is required; the abstract classes and interfaces can be integrated directly into projects as they are.

## Usage
### Interfaces
- **IAggregateRoot**: Represents an aggregate root that can hold domain events.
- **IAuditableEntity**: Defines properties for tracking creation and modification timestamps.
- **IDeletableEntity**: Marks entities that can be deleted and provides the deletion timestamp.
- **IDomainEvent**: Represents a domain event with a unique identifier.

### Abstract classes
- **AggregateRoot<TId>**: Abstract base class for aggregate roots that implements the `IAggregateRoot` interface.
- **DomainEvent**: Abstract class representing a domain event that implements `IDomainEvent`.
- **Entity<TId>**: Abstract base class for entities, implementing equality checks on their identifier.
- **Enumeration<TEnum>**: Provides a way to define enumerations with names and values.
- **ValueObject**: Abstract base class for value objects that implement equality based on their atomic values.

### Examples
In general the best way of utilizing these classes is to use the definied primitives (abstract classes) to make your domain behave as would be expected in domain-driven design.
1. AggregateRoot<TId>
    Defining an aggregate root properly is key to maintaining consistency in the domain model. The following ``Order`` class exemplifies this approach:
    ```csharp
    public class Order : AggregateRoot<int>
    {
        public string CustomerId { get; private set; }
        private List<OrderItem> _items = new List<OrderItem>();

        public Order(int id, string customerId)
            : base(id)
        {
            CustomerId = customerId;
        }

        public void AddItem(string productId, int quantity)
        {
            var orderItem = new OrderItem(productId, quantity);
            _items.Add(orderItem);
            RaiseDomainEvent(new OrderItemAddedEvent(Id, orderItem));
        }
    }
    ```
2. DomainEvent
    The DomainEvent class represents an event that occurs within the domain. It allows you to encapsulate the information related to changes in your domain model.
    ```csharp
    public class OrderItemAddedEvent : DomainEvent
    {
        public int OrderId { get; }
        public OrderItem OrderItem { get; }

        public OrderItemAddedEvent(int orderId, OrderItem orderItem)
            : base(Guid.NewGuid())
        {
            OrderId = orderId;
            OrderItem = orderItem;
        }
    }
    ```
3. Entity<TId>
    The Entity<TId> class provides a base implementation for entities, including equality checks based on their identifier. This ensures that entities are compared correctly throughout your application.
    ```csharp
    public class OrderItem : Entity<int>
    {
        public string ProductId { get; private set; }
        public int Quantity { get; private set; }

        public OrderItem(int id, string productId, int quantity)
            : base(id)
        {
            ProductId = productId;
            Quantity = quantity;
        }
    }
    ```
4. Enumeration<TEnum>
    The Enumeration<TEnum> class allows you to create strongly-typed enumerations with associated names and values (also called "Smart Enums"). The main advantage of using these "Smart Enums" over the traditional enums in C# is the ability to encapsulate business logic within the enums itself.
    ```csharp
    public class OrderStatus : Enumeration<OrderStatus>
    {
        public static readonly OrderStatus Pending = new(1, "Pending");
        public static readonly OrderStatus Shipped = new(2, "Shipped");
        public static readonly OrderStatus Delivered = new(3, "Delivered");

        private OrderStatus(int value, string name) : base(value, name) { }
    }
    ```
5. ValueObject
    The ValueObject class is used for creating immutable value objects that are defined by their properties rather than a unique identity. This is useful for concepts like monetary values or address information.
    ```csharp
    public class Money : ValueObject
    {
        public decimal Amount { get; }
        public string Currency { get; }

        public Money(decimal amount, string currency)
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
## Sample projects
The ``IAggregateRoot``, ``IAuditableEntity``, and ``IDeletableEntity`` abstractions are further used in the sister project **Resrcify.SharedKernel.UnitOfWork** where they are used in custom EntityFramework Interceptors, to utalizint the change tracker to automatically insert domain events in an outbox table, as well as update datetime properties and state on entities that implements the abstractions. Please see [InsertOutboxMessagesInterceptor.cs](../Resrcify.SharedKernel.UnitOfWork/Interceptors/InsertOutboxMessagesInterceptor.cs), [UpdateAuditableEntitiesInterceptor.cs](../Resrcify.SharedKernel.UnitOfWork/Interceptors/UpdateAuditableEntitiesInterceptor.cs), and [UpdateDeletableEntitiesInterceptor.cs](../Resrcify.SharedKernel.UnitOfWork/Interceptors/UpdateDeletableEntitiesInterceptor.cs).

In order to create a more uniformed repository pattern, a generic type constraints has been added to force the classes that make up your domain models to implement IAggregateRoot. Please see [Repository.cs](../Resrcify.SharedKernel.Repository/Primitives/Repository.cs).

Furthermore a sample project [**Resrcify.SharedKernel.WebApiExample**](../../samples/Resrcify.SharedKernel.WebApiExample) has been made showcasing how the domain layer can be built by using the building blocks of this repository.

## Suggestions for further development

Here are a few ideas for extending this library in the future:

- **Event Sourcing Support:** Implement support for event sourcing patterns.
- **Domain Service Abstractions:** Introduce abstractions for domain services that operate on aggregates.
- **Repository Interfaces:** While generally not supported by Clean Architecture, there are major benefits that be had by providing a generic repository interfaces for aggregate persistence.