using System.Diagnostics.CodeAnalysis;
using Quartz;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.BackgroundJobs;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class OutboxJobsTests
{
    [Fact]
    public void IsOutboxJob_ShouldKnowEveryContextsProcessingAndCleanupJobs()
    {
        OutboxJobs.IsOutboxJob(OutboxJobs.Process<TestDbContext>()).ShouldBeTrue();
        OutboxJobs.IsOutboxJob(OutboxJobs.Cleanup<TestDbContext>()).ShouldBeTrue();
        OutboxJobs.IsOutboxJob(OutboxJobs.Process<SecondTestDbContext>()).ShouldBeTrue();
    }

    [Fact]
    public void IsOutboxJob_ShouldNotTakeAnyOtherJobForOne()
    {
        OutboxJobs.IsOutboxJob(IntervalJobSetup.JobKeyFor<NoopJob>()).ShouldBeFalse();
        OutboxJobs.IsOutboxJob(new JobKey(OutboxJobs.Process<TestDbContext>().Name, "reports")).ShouldBeFalse();
        OutboxJobs.IsOutboxJob(new JobKey("ProcessOutboxMessagesJob")).ShouldBeFalse();
    }
}
