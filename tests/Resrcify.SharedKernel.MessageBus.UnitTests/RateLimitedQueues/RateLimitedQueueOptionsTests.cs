using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Rebus.Transport.InMem;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.MessageBus.Abstractions;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Extensions;
using Resrcify.SharedKernel.MessageBus.RateLimitedQueues;
using Resrcify.SharedKernel.MessageBus.UnitTests.Support;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.RateLimitedQueues;

/// <summary>
/// A queue's <see cref="RateLimitedQueueOptions"/> are the named options of its name, read when the host starts: the
/// registration's <c>configure</c> gives the defaults, and the service's own options pattern, registered after
/// <c>AddMessageBus</c>, configures them further from the container.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class RateLimitedQueueOptionsTests
{
    private const string Arena = "arena";
    private const string Profile = "profile";
    private const string Guild = "guild";
    private const string Tournament = "tournament";
    private const string Leaderboard = "leaderboard";

    [Fact]
    public void NameFor_ShouldBeTheNameTheQueueIsRegisteredUnder()
    {
        var services = new ServiceCollection();
        services.AddMessageBus(bus => bus
            .UseInMemory(new InMemNetwork())
            .AddRateLimitedQueue<NamedQuestion, NamedAnswer, NamedResponder>());

        var registered = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<RateLimitedQueueName>()
            .Single();

        RateLimitedQueueOptions.NameFor<NamedQuestion>().ShouldBe(nameof(NamedQuestion));
        registered.Name.ShouldBe(RateLimitedQueueOptions.NameFor<NamedQuestion>());
    }

    [Fact]
    public async Task AddRateLimitedQueue_ShouldUseTheConfigureAction_WhenNothingElseConfiguresTheQueue()
    {
        var recorder = new RecordingStrategy();
        using var host = await StartAsync(bus => bus.AddRateLimitedQueue<Question, Answer, Responder>(
            Guild,
            queue =>
            {
                queue.PerSecond = 25;
                queue.Burst = 3;
                queue.Prefetch = 7;
                queue.RateLimiter = recorder;
            }));

        var used = await recorder.OptionsOfAsync(Guild);

        used.PerSecond.ShouldBe(25);
        used.Burst.ShouldBe(3);
        used.Prefetch.ShouldBe(7);
        host.Services.GetRequiredService<IOptionsMonitor<RateLimitedQueueOptions>>().Get(Guild).ShouldBeSameAs(used);
    }

    [Fact]
    public async Task AddRateLimitedQueue_ShouldReadItsOptionsWhenTheHostStarts_NotWhenItIsRegistered()
    {
        var recorder = new RecordingStrategy();
        var perSecond = 5;
        using var host = await StartAsync(
            bus => bus.AddRateLimitedQueue<Question, Answer, Responder>(
                Guild,
                queue =>
                {
                    queue.PerSecond = perSecond;
                    queue.RateLimiter = recorder;
                }),
            after: services =>
            {
                // Both come after AddMessageBus: the queue's configure hasn't run yet, and the service's runs after it.
                perSecond = 7;
                services.Configure<RateLimitedQueueOptions>(Guild, queue => queue.Burst = 4);
            });

        var used = await recorder.OptionsOfAsync(Guild);

        used.PerSecond.ShouldBe(7);
        used.Burst.ShouldBe(4);
    }

    [Fact]
    public async Task BindConfiguration_ShouldWinOverTheRegistrationsDefaults_WhenTheServiceRegistersItAfterAddMessageBus()
    {
        var recorder = new RecordingStrategy();
        using var host = await StartAsync(
            bus => bus.AddRateLimitedQueue<Question, Answer, Responder>(
                Guild,
                queue =>
                {
                    queue.Prefetch = 4;
                    queue.HealthCheckInterval = TimeSpan.FromSeconds(15);
                    queue.RateLimiter = recorder;
                }),
            after: services => services
                .AddOptions<RateLimitedQueueOptions>(Guild)
                .BindConfiguration("Queues:Guild", binder => binder.ErrorOnUnknownConfiguration = true),
            settings: new Dictionary<string, string?> { ["Queues:Guild:Prefetch"] = "12" });

        var used = await recorder.OptionsOfAsync(Guild);

        used.Prefetch.ShouldBe(12);
        used.HealthCheckInterval.ShouldBe(TimeSpan.FromSeconds(15));   // not in the section: the default stays
    }

    [Fact]
    public async Task AddRateLimitedQueue_ShouldWinOverTheService_WhenTheServiceConfiguresTheQueueBeforeAddMessageBus()
    {
        var recorder = new RecordingStrategy();
        using var host = await StartAsync(
            bus => bus.AddRateLimitedQueue<Question, Answer, Responder>(
                Guild,
                queue =>
                {
                    queue.Prefetch = 4;
                    queue.RateLimiter = recorder;
                }),
            before: services => services.Configure<RateLimitedQueueOptions>(Guild, queue => queue.Prefetch = 12));

        (await recorder.OptionsOfAsync(Guild)).Prefetch.ShouldBe(4);
    }

    [Fact]
    public async Task Configure_ShouldSetTheQueueFromTheServicesTypedOptions_WhenItTakesThemFromTheContainer()
    {
        var recorder = new RecordingStrategy();
        using var host = await StartAsync(
            bus => bus.AddRateLimitedQueue<Question, Answer, Responder>(Guild),
            after: services =>
            {
                services
                    .AddOptions<GameQueues>()
                    .BindConfiguration("GalaxyOfHeroes:Queues", binder => binder.ErrorOnUnknownConfiguration = true)
                    .ValidateOnStart();
                services.AddSingleton(recorder);
                services
                    .AddOptions<RateLimitedQueueOptions>(Guild)
                    .Configure<IOptions<GameQueues>, RecordingStrategy>((queue, queues, strategy) =>
                    {
                        queue.RateLimiter = strategy;
                        queue.Prefetch = queues.Value.GetGuild.Prefetch;
                        queue.HealthCheckTag = queues.Value.HealthCheckTag;
                    });
            },
            settings: new Dictionary<string, string?>
            {
                ["GalaxyOfHeroes:Queues:GetGuild:Prefetch"] = "9",
                ["GalaxyOfHeroes:Queues:HealthCheckTag"] = "game",
            },
            healthy: true);

        var used = await recorder.OptionsOfAsync(Guild);

        used.RateLimiter.ShouldBeSameAs(recorder);
        used.Prefetch.ShouldBe(9);
        used.HealthCheckTag.ShouldBe("game");
    }

    [Fact]
    public async Task SharedRateLimiterStrategy_ShouldGiveEveryQueueOnABudgetOneLimiter_WhenTheBudgetComesFromTheContainer()
    {
        // SwgohApi's shape: the unauthenticated budget shared by two queues, the authenticated one by three, both built
        // from options.
        using var host = await StartAsync(
            bus => bus
                .AddRateLimitedQueue<Question, Answer, Responder>(Arena)
                .AddRateLimitedQueue<Question, Answer, Responder>(Profile)
                .AddRateLimitedQueue<Question, Answer, Responder>(Guild)
                .AddRateLimitedQueue<Question, Answer, Responder>(Tournament)
                .AddRateLimitedQueue<Question, Answer, Responder>(Leaderboard),
            after: services =>
            {
                services.Configure<BudgetLimits>(limits => limits.Authenticated = 18);
                services.AddSingleton(provider => new Budgets(provider.GetRequiredService<IOptions<BudgetLimits>>().Value));
                OnBudget(services, budgets => budgets.Unauthenticated, Arena, Profile);
                OnBudget(services, budgets => budgets.Authenticated, Guild, Tournament, Leaderboard);
            });
        var budgets = host.Services.GetRequiredService<Budgets>();
        var monitor = host.Services.GetRequiredService<IOptionsMonitor<RateLimitedQueueOptions>>();

        await InMemoryServices.WaitUntilAsync(() => host.Services.GetServices<IQueueConsumer>().All(queue => queue.IsConsuming));

        budgets.UnauthenticatedLimiters.ShouldBe(1);
        budgets.AuthenticatedLimiters.ShouldBe(1);
        budgets.AuthenticatedPerSecond.ShouldBe(18);
        monitor.Get(Arena).RateLimiter.ShouldBeSameAs(monitor.Get(Profile).RateLimiter);
        monitor.Get(Guild).RateLimiter.ShouldBeSameAs(monitor.Get(Tournament).RateLimiter);
        monitor.Get(Tournament).RateLimiter.ShouldBeSameAs(monitor.Get(Leaderboard).RateLimiter);
        monitor.Get(Arena).RateLimiter.ShouldNotBeSameAs(monitor.Get(Guild).RateLimiter);
    }

    [Fact]
    public async Task StartAsync_ShouldFailNamingTheQueueAndTheKey_WhenAQueuesOptionsAreInvalid()
    {
        var exception = await Should.ThrowAsync<OptionsValidationException>(() => StartAsync(
            bus => bus
                .AddRateLimitedQueue<Question, Answer, Responder>(Arena)
                .AddRateLimitedQueue<Question, Answer, Responder>(Guild),
            after: services => services
                .AddOptions<RateLimitedQueueOptions>(Guild)
                .BindConfiguration("Queues:Guild")
                .Configure(queue => queue.Burst = -1),
            settings: new Dictionary<string, string?> { ["Queues:Guild:PerSecond"] = "0" }));

        exception.OptionsName.ShouldBe(Guild);
        exception.Failures.ShouldBe(
        [
            "The rate-limited queue guild's PerSecond must be at least 1; it is 0.",
            "The rate-limited queue guild's Burst can't be negative; it is -1.",
        ]);
    }

    [Fact]
    public async Task StartAsync_ShouldFailNamingTheQueue_WhenTheRegistrationsOwnDefaultsAreInvalid()
    {
        var exception = await Should.ThrowAsync<OptionsValidationException>(() => StartAsync(
            bus => bus.AddRateLimitedQueue<NamedQuestion, NamedAnswer>(
                question => new NamedQuery(question),
                queue => queue.Prefetch = 0)));

        exception.Message.ShouldContain("The rate-limited queue NamedQuestion's Prefetch must be at least 1");
    }

    private static void OnBudget(
        IServiceCollection services,
        Func<Budgets, SharedRateLimiterStrategy> budget,
        params string[] queues)
    {
        foreach (var queue in queues)
        {
            services
                .AddOptions<RateLimitedQueueOptions>(queue)
                .Configure<Budgets>((options, budgets) =>
                {
                    options.RateLimiter = budget(budgets);
                    options.Prefetch = 2;
                });
        }
    }

    private static async Task<IHost> StartAsync(
        Action<MessageBusBuilder> configure,
        Action<IServiceCollection>? before = null,
        Action<IServiceCollection>? after = null,
        Dictionary<string, string?>? settings = null,
        bool healthy = false)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(settings ?? []);
        if (healthy)
            builder.Services.AddHealthChecks().AddCheck("game", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: ["game"]);
        before?.Invoke(builder.Services);
        builder.Services.AddMessageBus(bus => configure(bus.UseInMemory(new InMemNetwork())));
        after?.Invoke(builder.Services);
        var host = builder.Build();
        try
        {
            await host.StartAsync();
            return host;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    /// <summary>The options each queue's host created its limiter with.</summary>
    internal sealed class RecordingStrategy : IRateLimiterStrategy
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<RateLimitedQueueOptions>> _used = new(StringComparer.Ordinal);

        public RateLimiter CreateLimiter(string queueName, RateLimitedQueueOptions options)
        {
            Used(queueName).TrySetResult(options);
            return TokenBucketRateLimiterStrategy.Instance.CreateLimiter(queueName, options);
        }

        public Task<RateLimitedQueueOptions> OptionsOfAsync(string queueName)
            => Used(queueName).Task.WaitAsync(TimeSpan.FromSeconds(5));

        private TaskCompletionSource<RateLimitedQueueOptions> Used(string queueName)
            => _used.GetOrAdd(queueName, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    /// <summary>A service's typed options for its queues, bound from its own section.</summary>
    internal sealed class GameQueues
    {
        public string? HealthCheckTag { get; set; }

        public QueueSettings GetGuild { get; set; } = new();
    }

    internal sealed class QueueSettings
    {
        public int Prefetch { get; set; } = 4;
    }

    internal sealed class BudgetLimits
    {
        public int Unauthenticated { get; set; } = 90;

        public int Authenticated { get; set; } = 10;
    }

    /// <summary>Two budgets in one singleton, each counting the limiters it creates.</summary>
    internal sealed class Budgets
    {
        private int _unauthenticated;
        private int _authenticated;

        public Budgets(BudgetLimits limits)
        {
            AuthenticatedPerSecond = limits.Authenticated;
            Unauthenticated = new SharedRateLimiterStrategy(() =>
            {
                Interlocked.Increment(ref _unauthenticated);
                return Bucket(limits.Unauthenticated);
            });
            Authenticated = new SharedRateLimiterStrategy(() =>
            {
                Interlocked.Increment(ref _authenticated);
                return Bucket(limits.Authenticated);
            });
        }

        public SharedRateLimiterStrategy Unauthenticated { get; }

        public SharedRateLimiterStrategy Authenticated { get; }

        public int AuthenticatedPerSecond { get; }

        public int UnauthenticatedLimiters => Volatile.Read(ref _unauthenticated);

        public int AuthenticatedLimiters => Volatile.Read(ref _authenticated);

        private static RateLimiter Bucket(int perSecond)
            => TokenBucketRateLimiterStrategy.Instance.CreateLimiter(
                "shared",
                new RateLimitedQueueOptions { PerSecond = perSecond, Burst = 1 });
    }

    internal sealed record Question(string Key);

    internal sealed record Answer(string Key);

    internal sealed class Responder : IRequestResponder<Question, Answer>
    {
        public Task<Result<Answer>> HandleAsync(Question request, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success(new Answer(request.Key)));
    }

    internal sealed record NamedQuestion(string Key);

    internal sealed record NamedAnswer(string Key);

    internal sealed record NamedQuery(NamedQuestion Question) : IRequest<Result<NamedAnswer>>;

    internal sealed class NamedResponder : IRequestResponder<NamedQuestion, NamedAnswer>
    {
        public Task<Result<NamedAnswer>> HandleAsync(NamedQuestion request, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success(new NamedAnswer(request.Key)));
    }
}
