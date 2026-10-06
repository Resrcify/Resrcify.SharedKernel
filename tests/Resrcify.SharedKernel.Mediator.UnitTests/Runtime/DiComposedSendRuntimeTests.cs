using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Mediator.Extensions;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Mediator.UnitTests.Runtime;

/// <summary>Sending through the pipeline composed when the DI container builds it (<c>EnableDiTimePipelineComposition</c>).</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class DiComposedSendRuntimeTests
{
    [Fact]
    public async Task Send_ShouldRunTheBehaviorsAroundTheHandler_WhenThePipelineIsComposedByTheContainer()
    {
        using var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var log = scope.ServiceProvider.GetRequiredService<ComposedLog>();

        var response = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ComposedRequest(), CancellationToken.None);

        response.IsSuccess.ShouldBeTrue();
        log.Steps.ShouldBe(["Behavior:Before", "Handler", "Behavior:After"]);
    }

    [Fact]
    public async Task Send_ShouldCreateTheHandlerOnce_WhenThePipelineIsComposedByTheContainer()
    {
        using var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var log = scope.ServiceProvider.GetRequiredService<ComposedLog>();

        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ComposedRequest(), CancellationToken.None);

        log.HandlersCreated.ShouldBe(1);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<ComposedLog>();
        services.AddMediator(config =>
        {
            config.RegisterServicesFromAssemblies(typeof(DiComposedSendRuntimeTests).Assembly);
            config.EnableDiTimePipelineComposition();
        });
        services.AddTransient<IPipelineBehavior<ComposedRequest, Result>, ComposedBehavior>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class ComposedLog
    {
        public List<string> Steps { get; } = [];

        public int HandlersCreated { get; set; }
    }

    private sealed class ComposedRequest : IRequest<Result>;

    private sealed class ComposedRequestHandler : IRequestHandler<ComposedRequest, Result>
    {
        private readonly ComposedLog _log;

        public ComposedRequestHandler(ComposedLog log)
        {
            _log = log;
            _log.HandlersCreated++;
        }

        public Task<Result> Handle(ComposedRequest request, CancellationToken cancellationToken)
        {
            _log.Steps.Add("Handler");
            return Task.FromResult(Result.Success());
        }
    }

    // A closed behavior, registered in the container (the scan registers handlers only).
    private sealed class ComposedBehavior(ComposedLog log) : IPipelineBehavior<ComposedRequest, Result>
    {
        public async Task<Result> Handle(ComposedRequest request, RequestHandlerDelegate<Result> next, CancellationToken cancellationToken)
        {
            log.Steps.Add("Behavior:Before");
            var response = await next(cancellationToken);
            log.Steps.Add("Behavior:After");
            return response;
        }
    }
}
