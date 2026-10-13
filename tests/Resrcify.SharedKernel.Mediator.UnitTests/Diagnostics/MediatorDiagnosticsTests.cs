using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Mediator.Diagnostics;
using Resrcify.SharedKernel.Mediator.Extensions;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Mediator.UnitTests.Diagnostics;

/// <summary>
/// The span and duration the mediator records for every send. Listeners are process-wide, so each test looks only at
/// its own request type.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class MediatorDiagnosticsTests : IDisposable
{
    private readonly ConcurrentQueue<Activity> _activities = new();
    private readonly ConcurrentQueue<(double Seconds, Dictionary<string, object?> Tags)> _durations = new();
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener;
    private readonly FakeTimeProvider _clock = new();

    public MediatorDiagnosticsTests()
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MediatorDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(_activityListener);

        _meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MediatorDiagnostics.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            },
        };
        _meterListener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
            _durations.Enqueue((value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.Ordinal))));
        _meterListener.Start();
    }

    [Fact]
    public async Task Send_ShouldRecordASuccessfulSpanAndItsDuration_WhenTheRequestSucceeds()
    {
        await SendAsync(new DiagnosedSuccess());

        var activity = ActivityOf(nameof(DiagnosedSuccess));
        activity.GetTagItem("mediator.request.type").ShouldBe(typeof(DiagnosedSuccess).FullName);
        activity.GetTagItem("mediator.outcome").ShouldBe("success");
        activity.Status.ShouldBe(ActivityStatusCode.Unset);
        var (seconds, tags) = DurationOf(nameof(DiagnosedSuccess));
        tags["outcome"].ShouldBe("success");
        seconds.ShouldBe(0.25);
    }

    [Fact]
    public async Task Send_ShouldRecordTheErrorAndItsType_WhenTheRequestFails()
    {
        await SendAsync(new DiagnosedNotFound());

        var activity = ActivityOf(nameof(DiagnosedNotFound));
        activity.GetTagItem("mediator.outcome").ShouldBe("failure");
        activity.GetTagItem("mediator.error.code").ShouldBe("Shard.NotFound");
        activity.GetTagItem("mediator.error.type").ShouldBe("NotFound");
        activity.Status.ShouldBe(ActivityStatusCode.Unset);   // an expected answer, not an error
        DurationOf(nameof(DiagnosedNotFound)).Tags["outcome"].ShouldBe("NotFound");
    }

    [Fact]
    public async Task Send_ShouldMarkTheSpanAnError_WhenTheFailureIsTransient()
    {
        await SendAsync(new DiagnosedUpstreamDown());

        ActivityOf(nameof(DiagnosedUpstreamDown)).Status.ShouldBe(ActivityStatusCode.Error);
        DurationOf(nameof(DiagnosedUpstreamDown)).Tags["outcome"].ShouldBe("ExternalFailure");
    }

    [Fact]
    public async Task Send_ShouldRecordTheExceptionAndLetItThrough_WhenTheHandlerThrows()
    {
        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => SendAsync(new DiagnosedThrow()));

        thrown.Message.ShouldBe("The handler threw.");
        var activity = ActivityOf(nameof(DiagnosedThrow));
        activity.GetTagItem("mediator.outcome").ShouldBe("exception");
        activity.Status.ShouldBe(ActivityStatusCode.Error);
        activity.Events.ShouldContain(e => e.Name == "exception");
        DurationOf(nameof(DiagnosedThrow)).Tags["outcome"].ShouldBe("exception");
    }

    [Fact]
    public async Task Send_ShouldRecordTheSend_WhenTheRequestIsSentAsAnObject()
    {
        using var provider = BuildProvider();

        var response = await provider.GetRequiredService<ISender>().Send((object)new DiagnosedAsObject(), CancellationToken.None);

        response.ShouldBeOfType<Result>().IsSuccess.ShouldBeTrue();
        ActivityOf(nameof(DiagnosedAsObject)).GetTagItem("mediator.outcome").ShouldBe("success");
        DurationOf(nameof(DiagnosedAsObject)).Tags["outcome"].ShouldBe("success");
    }

    public void Dispose()
    {
        _activityListener.Dispose();
        _meterListener.Dispose();
    }

    private async Task SendAsync(IRequest<Result> request)
    {
        using var provider = BuildProvider();
        await provider.GetRequiredService<ISender>().Send(request, CancellationToken.None);
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddMediator(config => config.RegisterServicesFromAssemblies(typeof(MediatorDiagnosticsTests).Assembly));
        return services.BuildServiceProvider();
    }

    private Activity ActivityOf(string requestName)
        => _activities.Where(activity => activity.DisplayName == requestName).ShouldHaveSingleItem();

    private (double Seconds, Dictionary<string, object?> Tags) DurationOf(string requestName)
        => _durations.Single(duration => Equals(duration.Tags["request"], requestName));

    private sealed class DiagnosedSuccess : IRequest<Result>;

    private sealed class DiagnosedNotFound : IRequest<Result>;

    private sealed class DiagnosedUpstreamDown : IRequest<Result>;

    private sealed class DiagnosedThrow : IRequest<Result>;

    private sealed class DiagnosedAsObject : IRequest<Result>;

    private sealed class DiagnosedSuccessHandler(TimeProvider clock) : IRequestHandler<DiagnosedSuccess, Result>
    {
        public Task<Result> Handle(DiagnosedSuccess request, CancellationToken cancellationToken)
        {
            ((FakeTimeProvider)clock).Advance(TimeSpan.FromMilliseconds(250));
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class DiagnosedNotFoundHandler : IRequestHandler<DiagnosedNotFound, Result>
    {
        public Task<Result> Handle(DiagnosedNotFound request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Failure(Error.NotFound("Shard.NotFound", "No such shard.")));
    }

    private sealed class DiagnosedUpstreamDownHandler : IRequestHandler<DiagnosedUpstreamDown, Result>
    {
        public Task<Result> Handle(DiagnosedUpstreamDown request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Failure(Error.ExternalFailure("Upstream.Down", "The upstream is down.")));
    }

    private sealed class DiagnosedThrowHandler : IRequestHandler<DiagnosedThrow, Result>
    {
        public Task<Result> Handle(DiagnosedThrow request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The handler threw.");
    }

    private sealed class DiagnosedAsObjectHandler : IRequestHandler<DiagnosedAsObject, Result>
    {
        public Task<Result> Handle(DiagnosedAsObject request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }
}
