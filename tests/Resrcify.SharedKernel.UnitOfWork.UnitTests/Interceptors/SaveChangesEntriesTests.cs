using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Interceptors;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class SaveChangesEntriesTests : DbSetupBase
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _clock;
    private readonly FailOnceInterceptor _failOnce;
    private int _fullScans;

    public SaveChangesEntriesTests()
        : this(new FakeTimeProvider(Start), new FailOnceInterceptor())
    {
    }

    // The three SharedKernel interceptors as a service registers them, and one that can fail a save after them.
    private SaveChangesEntriesTests(FakeTimeProvider clock, FailOnceInterceptor failOnce)
        : base(
            new UpdateDeletableEntitiesInterceptor(clock),
            new UpdateAuditableEntitiesInterceptor(clock),
            new InsertOutboxMessagesInterceptor(new SystemTextJsonOutboxSerializer(), timeProvider: clock),
            failOnce)
    {
        _clock = clock;
        _failOnce = failOnce;
        DbContext.ChangeTracker.DetectedAllChanges += (_, _) => _fullScans++;
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldScanTheChangeTrackerTwice_WhenThreeInterceptorsRun()
    {
        // Arrange
        await DbContext.Persons.AddAsync(new Person(SocialSecurityNumber.Create(123456789), "John Doe"));
        _fullScans = 0;

        // Act
        await DbContext.SaveChangesAsync();

        // Assert: once for the interceptors, once in EF's own save.
        _fullScans.ShouldBe(2);
    }

    [Fact]
    public void SaveChanges_ShouldScanTheChangeTrackerTwice_WhenThreeInterceptorsRun()
    {
        // Arrange
        DbContext.Persons.Add(new Person(SocialSecurityNumber.Create(123456789), "John Doe"));
        _fullScans = 0;

        // Act
        DbContext.SaveChanges();

        // Assert
        _fullScans.ShouldBe(2);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldSeeChanges_WhenEntitiesWereChangedWithoutUpdate()
    {
        // Arrange
        var renamed = new Person(SocialSecurityNumber.Create(123456789), "John Doe");
        var removed = new Person(SocialSecurityNumber.Create(987654321), "Jane Doe");
        await DbContext.Persons.AddRangeAsync(renamed, removed);
        await DbContext.SaveChangesAsync();
        _clock.Advance(TimeSpan.FromMinutes(5));

        // Act: a property set on a tracked entity is only found by detecting changes.
        renamed.Name = "Johnny";
        DbContext.Persons.Remove(removed);
        await DbContext.SaveChangesAsync();

        // Assert
        renamed.ModifiedOnUtc.ShouldBe(Start.UtcDateTime.AddMinutes(5));
        renamed.CreatedOnUtc.ShouldBe(Start.UtcDateTime);
        removed.IsDeleted.ShouldBeTrue();
        removed.DeletedOnUtc.ShouldBe(Start.UtcDateTime.AddMinutes(5));
        removed.ModifiedOnUtc.ShouldBe(Start.UtcDateTime.AddMinutes(5));
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldDetectAgain_WhenThePreviousSaveFailedInAnInterceptor()
    {
        // Arrange
        var person = new Person(SocialSecurityNumber.Create(123456789), "John Doe");
        await DbContext.Persons.AddAsync(person);
        await DbContext.SaveChangesAsync();
        _clock.Advance(TimeSpan.FromMinutes(5));
        _failOnce.FailNextSave = true;
        await Should.ThrowAsync<InvalidOperationException>(() => DbContext.SaveChangesAsync());
        _clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        person.Name = "Johnny";
        await DbContext.SaveChangesAsync();

        // Assert
        person.ModifiedOnUtc.ShouldBe(Start.UtcDateTime.AddMinutes(10));
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldNotDetectChanges_WhenAutomaticDetectionIsOff()
    {
        // Arrange
        var person = new Person(SocialSecurityNumber.Create(123456789), "John Doe");
        await DbContext.Persons.AddAsync(person);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.AutoDetectChangesEnabled = false;
        _fullScans = 0;

        // Act
        await DbContext.SaveChangesAsync();

        // Assert
        _fullScans.ShouldBe(0);
        DbContext.ChangeTracker.AutoDetectChangesEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldLeaveAutomaticDetectionOn_WhenItWasOn()
    {
        // Arrange
        await DbContext.Persons.AddAsync(new Person(SocialSecurityNumber.Create(123456789), "John Doe"));

        // Act
        await DbContext.SaveChangesAsync();

        // Assert
        DbContext.ChangeTracker.AutoDetectChangesEnabled.ShouldBeTrue();
    }

    private sealed class FailOnceInterceptor
        : SaveChangesInterceptor
    {
        public bool FailNextSave { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!FailNextSave)
                return ValueTask.FromResult(result);

            FailNextSave = false;
            throw new InvalidOperationException("This save fails after the SharedKernel interceptors ran.");
        }
    }
}
