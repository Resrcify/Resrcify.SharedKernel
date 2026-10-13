using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.UnitOfWork.Converters;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;
using static Resrcify.SharedKernel.UnitOfWork.UnitTests.Models.VoScenario;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Extensions;

/// <summary>
/// A service switching from hand-written <c>HasConversion(x =&gt; x.Value, v =&gt; X.Create(v).Value)</c> to the
/// convention must see no difference: the same model (so no migration), the same SQL, the same rows, the same change
/// tracking and the same failure on a stored value that no longer validates.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class SingleValueObjectConventionExtensionsTests
    : IAsyncLifetime,
    IDisposable
{
    private const string Npgsql = "Npgsql";
    private const string Sqlite = "Sqlite";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public static TheoryData<string> Providers => [Npgsql, Sqlite];

    public static TheoryData<string> Queries => [.. PlayerQueries.Keys];

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var context = CreateOnSqlite<ExplicitConversionDbContext>();
        await context.Database.EnsureCreatedAsync();
        await SeedAsync(context);
    }

    public Task DisposeAsync()
        => Task.CompletedTask;

    public void Dispose()
        => _connection.Dispose();

    [Theory]
    [MemberData(nameof(Providers))]
    public void AddSingleValueObjectConversions_ShouldNeedNoMigration_FromHandWrittenConversions(string provider)
    {
        using var explicitContext = CreateModelOnly<ExplicitConversionDbContext>(provider);
        using var conventionContext = CreateModelOnly<ConventionDbContext>(provider);
        var differ = conventionContext.GetService<IMigrationsModelDiffer>();

        differ.GetDifferences(RelationalModel(explicitContext), RelationalModel(conventionContext)).ShouldBeEmpty();
        differ.GetDifferences(RelationalModel(conventionContext), RelationalModel(explicitContext)).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void AddSingleValueObjectConversions_ShouldWriteTheSameModelSnapshot_AsHandWrittenConversions(string provider)
    {
        using var explicitContext = CreateModelOnly<ExplicitConversionDbContext>(provider);
        using var conventionContext = CreateModelOnly<ConventionDbContext>(provider);

        // What `dotnet ef migrations add` compares against: equal snapshots mean an empty migration.
        Snapshot(conventionContext).ShouldBe(Snapshot(explicitContext));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void AddSingleValueObjectConversions_ShouldGiveEveryPropertyTheSameFacets_AsHandWrittenConversions(string provider)
    {
        using var explicitContext = CreateModelOnly<ExplicitConversionDbContext>(provider);
        using var conventionContext = CreateModelOnly<ConventionDbContext>(provider);

        var explicitFacets = Facets(explicitContext);
        var conventionFacets = Facets(conventionContext);

        conventionFacets.ShouldBe(explicitFacets);
        explicitFacets.ShouldContain(facet => facet.StartsWith("VoPlayer.Id:", StringComparison.Ordinal) && facet.Contains("key", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Queries))]
    public void AddSingleValueObjectConversions_ShouldTranslateQueries_ToTheSameSql(string query)
    {
        foreach (var provider in new[] { Npgsql, Sqlite })
        {
            using var explicitContext = CreateModelOnly<ExplicitConversionDbContext>(provider);
            using var conventionContext = CreateModelOnly<ConventionDbContext>(provider);

            PlayerQueries[query](conventionContext).ToQueryString()
                .ShouldBe(PlayerQueries[query](explicitContext).ToQueryString());
        }
    }

    [Theory]
    [MemberData(nameof(Queries))]
    public async Task AddSingleValueObjectConversions_ShouldReturnTheSameRows_AsHandWrittenConversions(string query)
    {
        await using var explicitContext = CreateOnSqlite<ExplicitConversionDbContext>();
        await using var conventionContext = CreateOnSqlite<ConventionDbContext>();

        var expected = Describe(await PlayerQueries[query](explicitContext).ToListAsync());
        var actual = Describe(await PlayerQueries[query](conventionContext).ToListAsync());

        actual.ShouldBe(expected);
        actual.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task AddSingleValueObjectConversions_ShouldFindByKey_AsHandWrittenConversions()
    {
        await using var explicitContext = CreateOnSqlite<ExplicitConversionDbContext>();
        await using var conventionContext = CreateOnSqlite<ConventionDbContext>();

        var expected = await explicitContext.Players.FindAsync(Second);
        var actual = await conventionContext.Players.FindAsync(Second);

        Describe([actual!]).ShouldBe(Describe([expected!]));
    }

    [Fact]
    public async Task AddSingleValueObjectConversions_ShouldReadWhatHandWrittenConversionsWrote_AndBack()
    {
        await using (var conventionContext = CreateOnSqlite<ConventionDbContext>())
        {
            var third = new VoPlayer(PlayerId.Create("P3").Value, AllyCode.Create(444_444_444).Value)
            {
                SimulatedPlayerId = First,
                Address = new VoAddress { ForwardTo = Second },
                Friends = [First],
            };
            third.Memberships.Add(new VoMembership(
                OrderNumber.Create(9).Value,
                third.Id,
                TrackingId.Create(Guid.NewGuid()).Value));
            conventionContext.Players.Add(third);
            await conventionContext.SaveChangesAsync();
        }

        await using var explicitContext = CreateOnSqlite<ExplicitConversionDbContext>();
        await using var readBack = CreateOnSqlite<ConventionDbContext>();
        var written = await explicitContext.Players.Include(player => player.Memberships).OrderBy(player => player.Id).ToListAsync();
        var read = await readBack.Players.Include(player => player.Memberships).OrderBy(player => player.Id).ToListAsync();

        Describe(read).ShouldBe(Describe(written));
        written.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddSingleValueObjectConversions_ShouldTrackChanges_AsHandWrittenConversions(bool convention)
    {
        await using VoDbContext context = convention
            ? CreateOnSqlite<ConventionDbContext>()
            : CreateOnSqlite<ExplicitConversionDbContext>();
        var player = await context.Players.SingleAsync(player => player.Id == Second);

        // An equal value object (another instance) is no change; another value is.
        player.AllyCode = AllyCode.Create(SecondAllyCode.Value).Value;
        player.SimulatedPlayerId = PlayerId.Create(First.Value).Value;
        context.ChangeTracker.DetectChanges();
        context.Entry(player).State.ShouldBe(EntityState.Unchanged);

        player.AllyCode = AllyCode.Create(555_555_555).Value;
        context.ChangeTracker.DetectChanges();
        context.Entry(player).Property(x => x.AllyCode).IsModified.ShouldBeTrue();
        context.Entry(player).Property(x => x.SimulatedPlayerId).IsModified.ShouldBeFalse();
    }

    [Fact]
    public async Task AddSingleValueObjectConversions_ShouldThrowOnAStoredValueThatNoLongerValidates_AsCreateValueDid()
    {
        await using (var context = CreateOnSqlite<ExplicitConversionDbContext>())
            await context.Database.ExecuteSqlRawAsync("UPDATE \"Players\" SET \"AllyCode\" = 5 WHERE \"Id\" = 'P1'");

        await using var explicitContext = CreateOnSqlite<ExplicitConversionDbContext>();
        await using var conventionContext = CreateOnSqlite<ConventionDbContext>();

        var expected = await Should.ThrowAsync<InvalidOperationException>(() => explicitContext.Players.ToListAsync());
        var actual = await Should.ThrowAsync<InvalidOperationException>(() => conventionContext.Players.ToListAsync());

        actual.Message.ShouldBe(expected.Message);
    }

    [Fact]
    public async Task AddSingleValueObjectConversions_ShouldReadThroughFromPersisted_WhenTheValueObjectImplementsIt()
    {
        await using (var context = CreateOnSqlite<ExplicitConversionDbContext>())
            await context.Database.ExecuteSqlRawAsync("UPDATE \"Players\" SET \"Code\" = 'TOOLONG' WHERE \"Id\" = 'P1'");

        await using var explicitContext = CreateOnSqlite<ExplicitConversionDbContext>();
        await using var conventionContext = CreateOnSqlite<ConventionDbContext>();

        await Should.ThrowAsync<InvalidOperationException>(() => explicitContext.Players.ToListAsync());
        var player = await conventionContext.Players.SingleAsync(player => player.Id == First);
        player.Code!.Value.ShouldBe("TOOLONG");
    }

    [Fact]
    public void AddSingleValueObjectConversions_ShouldConvertOnlyOptedInTypes()
    {
        using var context = CreateModelOnly<ConventionDbContext>(Npgsql);
        var player = context.Model.FindEntityType(typeof(VoPlayer))!;

        player.FindProperty(nameof(VoPlayer.Id))!.GetValueConverter().ShouldBeOfType<SingleValueObjectConverter<PlayerId, string>>();
        player.FindProperty(nameof(VoPlayer.AllyCode))!.GetValueConverter().ShouldBeOfType<SingleValueObjectConverter<AllyCode, long>>();
        player.FindProperty(nameof(VoPlayer.FormerAllyCode))!.GetValueConverter().ShouldBeOfType<SingleValueObjectConverter<AllyCode, long>>();
        player.FindComplexProperty(nameof(VoPlayer.Stats))!.ComplexType
            .FindProperty(nameof(VoStats.Referrer))!.GetValueConverter().ShouldBeOfType<SingleValueObjectConverter<AllyCode, long>>();
        player.FindNavigation(nameof(VoPlayer.Address))!.TargetEntityType
            .FindProperty(nameof(VoAddress.ForwardTo))!.GetValueConverter().ShouldBeOfType<SingleValueObjectConverter<PlayerId, string>>();
        context.Model.FindEntityType(typeof(VoBadge))!
            .FindProperty(nameof(VoBadge.Id))!.GetValueConverter().ShouldBeOfType<SingleValueObjectConverter<TrackingId, Guid>>();

        var legacy = player.FindProperty(nameof(VoPlayer.Legacy))!.GetValueConverter()!;
        legacy.ModelClrType.ShouldBe(typeof(LegacyCode));
        legacy.GetType().IsGenericType.ShouldBeTrue();
        legacy.GetType().GetGenericTypeDefinition().ShouldNotBe(typeof(SingleValueObjectConverter<,>));
        player.FindNavigation(nameof(VoPlayer.Address))!.TargetEntityType
            .FindProperty(nameof(VoAddress.Street))!.GetValueConverter().ShouldBeNull();
    }

    [Fact]
    public async Task AddSingleValueObjectConversions_ShouldLetAPropertysOwnConversionWin()
    {
        await using var context = CreateOnSqlite<OverridingConventionDbContext>();
        var simulated = context.Model.FindEntityType(typeof(VoPlayer))!.FindProperty(nameof(VoPlayer.SimulatedPlayerId))!;
        simulated.GetValueConverter().ShouldNotBeOfType<SingleValueObjectConverter<PlayerId, string>>();

        context.Players.Add(new VoPlayer(PlayerId.Create("P3").Value, FormerAllyCode) { SimulatedPlayerId = Second });
        await context.SaveChangesAsync();

        var stored = await context.Database
            .SqlQueryRaw<string>("SELECT \"SimulatedPlayerId\" AS \"Value\" FROM \"Players\" WHERE \"Id\" = 'P3'")
            .SingleAsync();
        stored.ShouldBe(OverridingConventionDbContext.Prefix + Second.Value);
    }

    [Fact]
    public void AddSingleValueObjectConversions_ShouldRefuse_WhenGivenNoAssembly()
        => Should.Throw<ArgumentException>(() => ModelConfigurationBuilderProbe.Run(builder =>
            builder.AddSingleValueObjectConversions(Array.Empty<Assembly>())));

    [Fact]
    public void AddSingleValueObjectConversions_ShouldRefuse_ATypeOptedInForTwoValueTypes()
    {
        var exception = Should.Throw<InvalidOperationException>(() => ModelConfigurationBuilderProbe.Run(builder =>
            builder.AddSingleValueObjectConversions([typeof(TwiceStored<int>)])));

        exception.Message.ShouldContain("more than one value type");
    }

    private TContext CreateOnSqlite<TContext>()
        where TContext : VoDbContext
        => CreateContext<TContext>(new DbContextOptionsBuilder<TContext>().UseSqlite(_connection).Options);

    private static TContext CreateModelOnly<TContext>(string provider)
        where TContext : VoDbContext
    {
        var options = new DbContextOptionsBuilder<TContext>();
        if (provider == Npgsql)
            options.UseNpgsql("Host=localhost;Database=model_only");
        else
            options.UseSqlite("DataSource=:memory:");
        return CreateContext<TContext>(options.Options);
    }

    private static TContext CreateContext<TContext>(DbContextOptions options)
        where TContext : VoDbContext
        => (TContext)Activator.CreateInstance(typeof(TContext), options)!;

    private static IRelationalModel RelationalModel(DbContext context)
        => context.GetService<IDesignTimeModel>().Model.GetRelationalModel();

    private static string Snapshot(DbContext context)
    {
        using var services = DesignTimeServices.For(context);
        var generator = services.GetRequiredService<IMigrationsCodeGeneratorSelector>().Select(language: null);
        return generator.GenerateSnapshot(
            "Snapshots",
            typeof(VoDbContext),
            "VoModelSnapshot",
            context.GetService<IDesignTimeModel>().Model);
    }

    private static List<string> Facets(DbContext context)
    {
        var model = context.GetService<IDesignTimeModel>().Model;
        var facets = new List<string>();
        foreach (var entityType in model.GetEntityTypes().OrderBy(entityType => entityType.Name, StringComparer.Ordinal))
        {
            foreach (var property in entityType.GetFlattenedProperties())
                facets.Add(Facet(entityType, property));
            foreach (var key in entityType.GetKeys())
                facets.Add($"{entityType.DisplayName()} key {key.GetName()}: {string.Join(", ", key.Properties.Select(p => p.Name))}");
            foreach (var index in entityType.GetIndexes())
                facets.Add($"{entityType.DisplayName()} index {index.GetDatabaseName()}: {string.Join(", ", index.Properties.Select(p => p.Name))} unique={index.IsUnique}");
            foreach (var foreignKey in entityType.GetForeignKeys())
                facets.Add($"{entityType.DisplayName()} fk {foreignKey.GetConstraintName()}: {string.Join(", ", foreignKey.Properties.Select(p => p.Name))} -> {foreignKey.PrincipalEntityType.DisplayName()}");
        }
        return facets;
    }

    private static string Facet(
        IEntityType entityType,
        IProperty property)
    {
        var mapping = property.GetRelationalTypeMapping();
        var element = property.GetElementType();
        return string.Join(
            " ",
            $"{entityType.DisplayName()}.{property.Name}:",
            $"clr={property.ClrType.Name}",
            $"provider={mapping.Converter?.ProviderClrType.Name ?? mapping.ClrType.Name}",
            $"column={property.GetColumnName()}",
            $"store={property.GetColumnType()}",
            $"nullable={property.IsNullable}",
            $"maxLength={property.GetMaxLength()}",
            $"generated={property.ValueGenerated}",
            property.IsKey() ? "key" : "",
            property.IsForeignKey() ? "fk" : "",
            property.IsIndex() ? "index" : "",
            $"comparer={property.GetValueComparer().Type.Name}",
            $"providerComparer={property.GetProviderValueComparer().Type.Name}",
            element is null
                ? ""
                : $"element={element.ClrType.Name}/{element.GetTypeMapping().Converter?.ProviderClrType.Name}/{element.GetMaxLength()}");
    }

    /// <summary>Runs a convention configuration the way EF Core does, by building a model that uses it.</summary>
    private static class ModelConfigurationBuilderProbe
    {
        public static void Run(Action<ModelConfigurationBuilder> configure)
        {
            using var context = new ProbeDbContext(configure);
            _ = context.Model;
        }

        private sealed class ProbeDbContext(Action<ModelConfigurationBuilder> configure)
            : DbContext(new DbContextOptionsBuilder<ProbeDbContext>()
                .UseSqlite("DataSource=:memory:")
                .ReplaceService<IModelCacheKeyFactory, UncachedModelKeyFactory>()
                .Options)
        {
            protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
                => configure(configurationBuilder);
        }

        private sealed class UncachedModelKeyFactory
            : IModelCacheKeyFactory
        {
            public object Create(DbContext context, bool designTime)
                => new object();
        }
    }
}
