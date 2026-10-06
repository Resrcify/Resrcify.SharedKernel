using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Resrcify.SharedKernel.UnitOfWork.Postgres.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.UnitTests.Extensions;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class PostgresRowVersionExtensionsTests
{
    [Fact]
    public void HasPostgresRowVersion_ShouldMapAShadowConcurrencyTokenToXmin()
    {
        using var context = CreateContext();

        var property = context.Model.FindEntityType(typeof(Order))!.FindProperty("xmin").ShouldNotBeNull();

        AssertMappedToXmin(property);
        property.IsShadowProperty().ShouldBeTrue();
    }

    [Fact]
    public void HasPostgresRowVersion_ShouldMapTheGivenPropertyToXmin()
    {
        using var context = CreateContext();

        var property = context.Model.FindEntityType(typeof(Invoice))!.FindProperty(nameof(Invoice.Version)).ShouldNotBeNull();

        AssertMappedToXmin(property);
        property.IsShadowProperty().ShouldBeFalse();
    }

    private static void AssertMappedToXmin(IProperty property)
    {
        property.ClrType.ShouldBe(typeof(uint));
        property.IsConcurrencyToken.ShouldBeTrue();
        property.ValueGenerated.ShouldBe(ValueGenerated.OnAddOrUpdate);
        property.GetColumnName().ShouldBe("xmin");
        property.GetColumnType().ShouldBe("xid");
    }

    private static TestDbContext CreateContext()
        => new(new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql("Host=localhost;Database=model;Username=postgres")
            .Options);
}
