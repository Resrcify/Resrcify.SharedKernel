using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Extensions;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class SaveChangesInterceptorExtensionsTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    [Fact]
    public async Task AddEntityInterceptors_ShouldStampAuditTimes_FromTheContainersTimeProvider()
    {
        await using var provider = await BuildAsync(services => services
            .AddSingleton<TimeProvider>(new FakeTimeProvider(Now))
            .AddEntityInterceptors());
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var person = new Person(SocialSecurityNumber.Create(400000001), "Audited");

        context.Persons.Add(person);
        await context.SaveChangesAsync();

        person.CreatedOnUtc.ShouldBe(Now.UtcDateTime);
    }

    [Fact]
    public async Task AddOutboxInterceptor_ShouldWriteEvents_WithTheRegisteredSerializer_AndTellTheObservers()
    {
        var observer = new RecordingObserver();
        await using var provider = await BuildAsync(services => services
            .AddSingleton<IOutboxSaveObserver>(observer)
            .AddOutboxInterceptor());
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var person = new TestAggregateRoot(SocialSecurityNumber.Create(400000002), "Evented");
        person.PublicRaiseDomainEvent(new TestDomainEvent(Guid.NewGuid(), "saved"));

        context.Persons.Add(person);
        await context.SaveChangesAsync();

        provider.GetRequiredService<IOutboxSerializer>().ShouldBeOfType<SystemTextJsonOutboxSerializer>();
        (await context.OutboxMessages.CountAsync()).ShouldBe(1);
        observer.Saved.ShouldHaveSingleItem().ShouldBe(1);
    }

    [Fact]
    public async Task AddSaveChangesInterceptors_ShouldLeaveTheOutboxOut_WhenToldTo()
    {
        await using var provider = await BuildAsync(
            services => services.AddEntityInterceptors().AddOutboxInterceptor(),
            withOutbox: false);
        await using var scope = provider.CreateAsyncScope();

        var interceptors = scope.ServiceProvider.GetRequiredService<TestDbContext>()
            .GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];

        interceptors.OfType<UpdateAuditableEntitiesInterceptor>().ShouldHaveSingleItem();
        interceptors.OfType<UpdateDeletableEntitiesInterceptor>().ShouldHaveSingleItem();
        interceptors.OfType<InsertOutboxMessagesInterceptor>().ShouldBeEmpty();
    }

    public async ValueTask DisposeAsync()
        => await _connection.DisposeAsync();

    private async Task<ServiceProvider> BuildAsync(
        Action<IServiceCollection> configure,
        bool withOutbox = true)
    {
        await _connection.OpenAsync();
        var services = new ServiceCollection();
        configure(services);
        services.AddDbContext<TestDbContext>((provider, options) => options
            .UseSqlite(_connection)
            .AddSaveChangesInterceptors(provider, withOutbox));
        var built = services.BuildServiceProvider();
        await using (var scope = built.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
        return built;
    }

    private sealed class TestAggregateRoot(SocialSecurityNumber id, string name)
        : Person(id, name)
    {
        public void PublicRaiseDomainEvent(IDomainEvent domainEvent)
            => RaiseDomainEvent(domainEvent);
    }

    private sealed class RecordingObserver : IOutboxSaveObserver
    {
        public List<int> Saved { get; } = [];

        public Task MessagesSavedAsync(
            DbContext context,
            IReadOnlyList<OutboxMessage> messages,
            CancellationToken cancellationToken)
        {
            Saved.Add(messages.Count);
            return Task.CompletedTask;
        }
    }
}
