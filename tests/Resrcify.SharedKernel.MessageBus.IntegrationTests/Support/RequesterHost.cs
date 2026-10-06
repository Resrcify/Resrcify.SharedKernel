using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.MessageBus.Abstractions;
using Resrcify.SharedKernel.Mediator.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Extensions;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.IntegrationTests.Support;

/// <summary>
/// A requesting service, wired the way a consumer already is (DbContext with the outbox interceptor
/// and Postgres dedup, the mediator, outbox processing, Quartz) plus <c>AddMessageBus(...).AddScatterGather()</c>.
/// Its handlers are found by the mediator scan, which <c>AddScatterGather</c> follows.
/// </summary>
internal sealed class RequesterHost : IAsyncDisposable
{
    private readonly IHost _host;

    private RequesterHost(IHost host)
        => _host = host;

    public static async Task<RequesterHost> StartAsync(
        RabbitMqConnection connection,
        string databaseConnectionString,
        string requestQueue,
        IBusConfigurationStrategy? configuration = null,
        Action<MessageBusBuilder>? configureBus = null)
    {
        var builder = Host.CreateApplicationBuilder();
        var outboxSerializer = new SystemTextJsonOutboxSerializer();
        builder.Services.AddDbContext<TestDbContext>(options => options
            .UseNpgsql(databaseConnectionString)
            .AddInterceptors(new InsertOutboxMessagesInterceptor(outboxSerializer, new PostgresOnConflictOutboxInsertStrategy())));
        builder.Services.AddMediator(typeof(RequesterHost).Assembly);
        builder.Services.AddOutboxLanes<TestDbContext>(lanes =>
        {
            lanes.PollInterval = TimeSpan.FromMilliseconds(200);
            lanes.Claim = PostgresOutboxLaneClaim.Instance;
        });
        builder.Services.AddOutboxProcessing<TestDbContext>(outboxSerializer, outbox =>
        {
            outbox.ProcessIntervalInSeconds = 1;
            outbox.DelayInSecondsBeforeStart = 0;
        });
        builder.Services.AddQuartzHostedService();
        builder.Services.AddMessageBus(bus =>
        {
            bus.UseRabbitMq(connection)
                .WithInputQueue($"requester-{Guid.NewGuid():N}")
                .AddMessage<PingRequest>(WireNames.PingRequest, sendTo: requestQueue)
                .AddMessage<PingResponse>(WireNames.PingResponse)
                .AddScatterGather();
            if (configuration is not null)
                bus.UseConfigurationStrategy(configuration);
            configureBus?.Invoke(bus);
        });

        var host = builder.Build();
        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
        await host.StartAsync();
        return new RequesterHost(host);
    }

    /// <summary>Starts a run and commits its unit of work, unless told not to (a rolled-back command).</summary>
    public async Task<Guid> StartRunAsync(int items, bool commit = true, string? dedupGroup = null)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var run = PingRun.Start(items, dedupGroup);
        scope.ServiceProvider.GetRequiredService<TestDbContext>().PingRuns.Add(run);
        if (commit)
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CompleteAsync();
        return run.Id;
    }

    public async Task<Guid> CreateNoteAsync(bool shared = false)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var note = shared ? Note.CreateShared() : Note.Create();
        scope.ServiceProvider.GetRequiredService<TestDbContext>().Notes.Add(note);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CompleteAsync();
        return note.Id;
    }

    /// <summary>Sends a mediator request, as the service's own code would.</summary>
    public async Task<TResponse> SendAsync<TResponse>(Resrcify.SharedKernel.Abstractions.Mediator.IRequest<TResponse> request)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<Resrcify.SharedKernel.Abstractions.Mediator.ISender>().Send(request);
    }

    /// <summary>Asks for one item directly, as code outside a scatter-gather handler would.</summary>
    public Task<Result<PingResponse>> RequestAsync(PingRequest request, TimeSpan timeout)
        => _host.Services.GetRequiredService<IScatterGatherClient>().RequestAsync<PingRequest, PingResponse>(request, timeout);

    public Task<PingRun?> FindRunAsync(Guid runId)
        => QueryAsync(context => context.PingRuns.AsNoTracking().FirstOrDefaultAsync(run => run.Id == runId));

    public Task<Note?> FindNoteAsync(Guid noteId)
        => QueryAsync(context => context.Notes.AsNoTracking().FirstOrDefaultAsync(note => note.Id == noteId));

    public Task<int> PendingScatterMessagesAsync()
        => QueryAsync(context => context.Set<OutboxMessage>()
            .CountAsync(message => message.Type == typeof(PingRequested).FullName && message.ProcessedOnUtc == null));

    /// <summary>Waits until the run's replies have been gathered (all answered, or its timeout passed).</summary>
    public async Task<PingRun> WaitUntilGatheredAsync(Guid runId, TimeSpan timeout)
    {
        var giveUpAt = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < giveUpAt)
        {
            if (await FindRunAsync(runId) is { GatheredAtUtc: not null } run)
                return run;
            await Task.Delay(100);
        }
        throw new TimeoutException($"Run {runId} was not gathered within {timeout}.");
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    private async Task<T> QueryAsync<T>(Func<TestDbContext, Task<T>> query)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<TestDbContext>());
    }

    /// <summary>
    /// The scatter-gather handler: one ping per item, then the counts recorded on the run. Found by the
    /// mediator scan; its event runs in the outbox's scatter-gather lane.
    /// </summary>
    internal sealed class PingScatterGatherHandler(TestDbContext context)
        : IScatterGatherHandler<PingRequested, PingRequest, PingResponse>
    {
        /// <summary>Tests run one at a time (one collection), so each can set its own timeout.</summary>
        public static TimeSpan CurrentTimeout { get; set; } = TimeSpan.FromSeconds(30);

        public TimeSpan Timeout => CurrentTimeout;

        /// <summary>The failures of the last gathered run, as the requester got them.</summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<Error>> LastFailures { get; private set; } = new Dictionary<string, IReadOnlyList<Error>>();

        public Task<IReadOnlyDictionary<string, PingRequest>> ScatterAsync(PingRequested notification, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(notification);
            return Task.FromResult<IReadOnlyDictionary<string, PingRequest>>(
                Enumerable.Range(0, notification.Items).ToDictionary(i => $"item-{i}", i => new PingRequest($"ping {i}")));
        }

        public async Task GatherAsync(PingRequested notification, IGathered<PingResponse> replies, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(notification);
            ArgumentNullException.ThrowIfNull(replies);
            var run = await context.PingRuns.FirstAsync(x => x.Id == notification.RunId, cancellationToken);
            LastFailures = replies.Failures;
            run.RecordGathered(replies.Results.Count, replies.Failures.Count, replies.UnansweredKeys.Count, DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Publishes a shared note to other services, then fails its first attempt after publishing, as a handler does when
    /// its transaction fails late: the outbox retries the message, and it publishes again.
    /// </summary>
    internal sealed class NoteSharedHandler(IEventBus eventBus)
        : IDomainEventHandler<NoteShared>
    {
        public static ConcurrentDictionary<Guid, int> Attempts { get; } = new();

        public async Task Handle(NoteShared notification, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(notification);
            await eventBus.PublishAsync(new PlayerRenamedPublished(notification.NoteId.ToString("N"), "shared"), cancellationToken);
            if (Attempts.AddOrUpdate(notification.NoteId, 1, (_, attempts) => attempts + 1) == 1)
                throw new InvalidOperationException("The first attempt fails after publishing.");
        }
    }

    /// <summary>An ordinary domain event handler, processed by the regular outbox job.</summary>
    internal sealed class NoteCreatedHandler(TestDbContext context)
        : IDomainEventHandler<NoteCreated>
    {
        public async Task Handle(NoteCreated notification, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(notification);
            var note = await context.Notes.FirstAsync(x => x.Id == notification.NoteId, cancellationToken);
            note.MarkHandled(DateTime.UtcNow);
        }
    }
}
