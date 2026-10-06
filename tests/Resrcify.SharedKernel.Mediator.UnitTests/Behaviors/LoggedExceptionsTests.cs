using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.Mediator.Behaviors;
using Resrcify.SharedKernel.Mediator.Configuration;
using Resrcify.SharedKernel.Mediator.Extensions;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Mediator.UnitTests.Behaviors;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class LoggedExceptionsTests
    : IDisposable
{
    private readonly RecordingLoggerProvider _logs = new();

    public void Dispose()
        => _logs.Dispose();

    [Fact]
    public async Task Send_ShouldLogTheExceptionAtErrorOnce_WithTheStandardBehaviors()
    {
        var thrown = await SendThrowingAsync(standard => { });

        var error = _logs.Entries.Where(entry => entry.Level == LogLevel.Error).ShouldHaveSingleItem();
        error.Exception.ShouldBeSameAs(thrown);
        error.Category.ShouldContain(nameof(UnitOfWorkPipelineBehavior<,>));
        error.Message.ShouldContain(nameof(ThrowingTransactionCommand));

        // The transaction and logging behaviors still say what happened, without the stack.
        _logs.Entries
            .Where(entry => entry.Level == LogLevel.Debug && entry.Exception is null)
            .Select(entry => entry.Category)
            .ShouldContain(category => category.Contains(nameof(TransactionPipelineBehavior<,>), StringComparison.Ordinal));
        _logs.Entries
            .Where(entry => entry.Level == LogLevel.Debug && entry.Exception is null)
            .Select(entry => entry.Category)
            .ShouldContain(category => category.Contains(nameof(LoggingPipelineBehavior<,>), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Send_ShouldStillLogTheException_WithoutTheLoggingBehavior()
    {
        var thrown = await SendThrowingAsync(standard => standard.Without(StandardBehavior.Logging));

        _logs.Entries
            .Where(entry => entry.Level == LogLevel.Error)
            .ShouldHaveSingleItem()
            .Exception.ShouldBeSameAs(thrown);
    }

    [Fact]
    public async Task Send_ShouldLogTheExceptionInTheLoggingBehavior_WhenNoOtherBehaviorSawIt()
    {
        await using var provider = BuildProvider(standard => { });
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => sender.Send(new ThrowingQuery(), CancellationToken.None));

        var error = _logs.Entries.Where(entry => entry.Level == LogLevel.Error).ShouldHaveSingleItem();
        error.Exception.ShouldBeSameAs(thrown);
        error.Category.ShouldContain(nameof(LoggingPipelineBehavior<,>));
    }

    private async Task<Exception> SendThrowingAsync(Action<StandardBehaviorsOptions> standard)
    {
        await using var provider = BuildProvider(standard);
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        return await Should.ThrowAsync<InvalidOperationException>(
            () => sender.Send(new ThrowingTransactionCommand(), CancellationToken.None));
    }

    private ServiceProvider BuildProvider(Action<StandardBehaviorsOptions> standard)
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<Result>>>(),
                Arg.Any<IsolationLevel>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<Result>>>().Invoke(CancellationToken.None));

        var services = new ServiceCollection();
        services.AddLogging(logging => logging
            .SetMinimumLevel(LogLevel.Debug)
            .AddProvider(_logs));
        services.AddScoped(_ => unitOfWork);
        services.AddMediator(cfg => cfg.AddStandardBehaviors(options => standard(options.Without(StandardBehavior.Caching))));
        // Open generic handlers, closed here: the assembly scan of other tests must not find a transactional command.
        services.AddTransient<IRequestHandler<ThrowingTransactionCommand, Result>, ThrowingCommandHandler<ThrowingTransactionCommand>>();
        services.AddTransient<IRequestHandler<ThrowingQuery, Result<string>>, ThrowingQueryHandler<ThrowingQuery>>();
        return services.BuildServiceProvider();
    }

    [SuppressMessage(
        "Maintainability",
        "CA1515:Consider making public types internal",
        Justification = "NSubstitute (which uses Castle DynamicProxy) cannot generate a mock of a type containing inaccessible generic parameters")]
    public sealed class ThrowingTransactionCommand
        : ITransactionCommand
    {
        public TimeSpan? CommandTimeout => null;
        public IsolationLevel? IsolationLevel => null;
    }

    [SuppressMessage(
        "Maintainability",
        "CA1515:Consider making public types internal",
        Justification = "NSubstitute (which uses Castle DynamicProxy) cannot generate a mock of a type containing inaccessible generic parameters")]
    public sealed class ThrowingQuery
        : IQuery<string>;

    private sealed class ThrowingCommandHandler<TCommand>
        : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public Task<Result> Handle(TCommand request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The command handler failed.");
    }

    private sealed class ThrowingQueryHandler<TQuery>
        : IQueryHandler<TQuery, string>
        where TQuery : IQuery<string>
    {
        public Task<Result<string>> Handle(TQuery request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The query handler failed.");
    }

    private sealed record LogEntry(
        string Category,
        LogLevel Level,
        string Message,
        Exception? Exception);

    private sealed class RecordingLoggerProvider
        : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyCollection<LogEntry> Entries
            => [.. _entries];

        public ILogger CreateLogger(string categoryName)
            => new RecordingLogger(categoryName, _entries);

        public void Dispose()
        {
            // Nothing to release.
        }

        private sealed class RecordingLogger(
            string category,
            ConcurrentQueue<LogEntry> entries)
            : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel)
                => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
                => entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception), exception));
        }
    }
}
