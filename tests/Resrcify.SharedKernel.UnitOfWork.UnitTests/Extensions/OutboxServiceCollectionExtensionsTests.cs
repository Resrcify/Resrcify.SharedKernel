using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Extensions;

/// <summary>Which serializer <c>AddOutboxProcessing</c> reads with: the registered one, never a silently ignored one.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class OutboxServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddOutboxProcessing_ShouldReadWithTheRegisteredSerializer_WhenGivenNone()
    {
        var registered = new CustomSerializer();
        var services = new ServiceCollection();
        services.AddSingleton<IOutboxSerializer>(registered);

        services.AddOutboxProcessing<TestDbContext>();

        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOutboxSerializer>().ShouldBeSameAs(registered);
    }

    [Fact]
    public async Task AddOutboxProcessing_ShouldRegisterTheSystemTextJsonSerializer_WhenNoneIsRegisteredOrGiven()
    {
        var services = new ServiceCollection();

        services.AddOutboxProcessing<TestDbContext>(outbox => outbox.ProcessIntervalInSeconds = 20);

        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOutboxSerializer>().ShouldBeOfType<SystemTextJsonOutboxSerializer>();
    }

    [Fact]
    public async Task AddOutboxProcessing_ShouldApplyTheOptions_WhenGivenOnlyThem()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddOutboxProcessing<TestDbContext>(outbox => outbox.BatchSize = 7);

        await using var provider = services.BuildServiceProvider();
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        var job = (await scheduler.GetJobDetail(OutboxJobs.Process<TestDbContext>())).ShouldNotBeNull();
        job.JobDataMap.TryGetValue("ProcessBatchSize", out var batchSize).ShouldBeTrue();
        batchSize.ShouldBe(7);
    }

    [Fact]
    public async Task AddOutboxProcessing_ShouldRegisterTheGivenSerializer_WhenNoneIsRegistered()
    {
        var given = new CustomSerializer();
        var services = new ServiceCollection();

        services.AddOutboxProcessing<TestDbContext>(given);

        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOutboxSerializer>().ShouldBeSameAs(given);
    }

    [Fact]
    public async Task AddOutboxProcessing_ShouldAcceptAnotherInstanceOfTheRegisteredType()
    {
        // What the services do: AddPostgresDbContext(... WithOutbox()) registers the built-in serializer, then
        // AddOutboxProcessing(new SystemTextJsonOutboxSerializer(), ...). The built-in one is stateless.
        var services = new ServiceCollection();
        services.AddOutboxInterceptor();

        Should.NotThrow(() => services.AddOutboxProcessing<TestDbContext>(new SystemTextJsonOutboxSerializer()));

        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOutboxSerializer>().ShouldBeOfType<SystemTextJsonOutboxSerializer>();
    }

    [Fact]
    public void AddOutboxProcessing_ShouldThrow_WhenGivenAnotherSerializerThanTheRegisteredOne()
    {
        var services = new ServiceCollection();
        services.AddOutboxInterceptor();

        var thrown = Should.Throw<InvalidOperationException>(
            () => services.AddOutboxProcessing<TestDbContext>(new CustomSerializer()));

        thrown.Message.ShouldContain("CustomSerializer");
        thrown.Message.ShouldContain("SystemTextJsonOutboxSerializer is registered already");
        thrown.Message.ShouldContain("Pass no serializer");
    }

    /// <summary>A serializer of its own type, as a service with another format would have.</summary>
    private sealed class CustomSerializer : IOutboxSerializer
    {
        public string Serialize(IDomainEvent domainEvent)
            => "{}";

        public IDomainEvent? Deserialize(string content)
            => null;
    }
}
