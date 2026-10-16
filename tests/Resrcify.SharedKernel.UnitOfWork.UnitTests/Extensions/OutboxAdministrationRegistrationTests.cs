using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Extensions;

/// <summary>The outbox registrations give each DbContext its <see cref="IOutboxAdministration{TContext}"/>.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class OutboxAdministrationRegistrationTests
{
    public static TheoryData<string> Registrations
        => ["AddOutboxProcessing", "AddOutboxLanes"];

    [Theory]
    [MemberData(nameof(Registrations))]
    public async Task TheOutboxRegistrations_ShouldRegisterTheAdministration_AsTheInterfaceAndItself(
        string registration)
    {
        var services = Services();
        Register(services, registration);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var administration = scope.ServiceProvider.GetRequiredService<IOutboxAdministration<TestDbContext>>();

        administration.ShouldBeSameAs(scope.ServiceProvider.GetRequiredService<OutboxAdministration<TestDbContext>>());
        administration.ContextType.ShouldBe(typeof(TestDbContext));
    }

    [Fact]
    public async Task AddOutboxProcessing_ShouldKeepTheServicesOwnAdministration()
    {
        var own = Substitute.For<IOutboxAdministration<TestDbContext>>();
        var services = Services();
        services.AddSingleton(own);

        services.AddOutboxProcessing<TestDbContext>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IOutboxAdministration<TestDbContext>>().ShouldBeSameAs(own);
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TestDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        return services;
    }

    private static void Register(
        IServiceCollection services,
        string registration)
    {
        if (registration == "AddOutboxProcessing")
            services.AddOutboxProcessing<TestDbContext>();
        else
            services.AddOutboxLanes<TestDbContext>();
    }
}
