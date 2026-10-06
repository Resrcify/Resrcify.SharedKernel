using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Resrcify.SharedKernel.UnitOfWork.Converters;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;
using static Resrcify.SharedKernel.UnitOfWork.UnitTests.Models.VoScenario;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Extensions;

/// <summary>
/// The convention in a compiled model (<c>dotnet ef dbcontext optimize</c>, then <c>UseModel(...Model.Instance)</c>):
/// EF Core generates the model's C# (see <see cref="CompiledModels"/>), which is compiled, loaded and used in place of the
/// model the context would build. It must read, write, query and track exactly as the model built at run time does,
/// both as <c>optimize</c> writes it by default and as it writes it for NativeAOT and precompiled queries
/// (<c>--native-aot</c>), where every converter is written out as C#.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class SingleValueObjectConventionExtensionsCompiledModelTests
    : IAsyncLifetime,
    IDisposable
{
    /// <summary><c>optimize</c> on SQLite, the provider the rows are read from.</summary>
    private const string Sqlite = "Sqlite";

    /// <summary><c>optimize --native-aot</c> on SQLite (on <see cref="NativeAotConventionDbContext"/>).</summary>
    private const string SqliteNativeAot = "SqliteNativeAot";

    /// <summary><c>optimize</c> on PostgreSQL: its SQL is compared, no rows are read.</summary>
    private const string Npgsql = "Npgsql";

    private const string ModelOnlyNpgsql = "Host=localhost;Database=model_only";

    // Each is generated, compiled and loaded once: an assembly name can be loaded only once.
    private static readonly ConcurrentDictionary<string, Lazy<IModel>> Models = new();

    // A database per SQLite variant, each created by its context (the NativeAOT one has no owned type's columns).
    private readonly SqliteConnection _sqlite = new("DataSource=:memory:");
    private readonly SqliteConnection _sqliteNativeAot = new("DataSource=:memory:");

    public static TheoryData<string> Queries => [.. PlayerQueries.Keys];

    public static TheoryData<string> OnSqlite => [Sqlite, SqliteNativeAot];

    public async Task InitializeAsync()
    {
        foreach (var variant in new[] { Sqlite, SqliteNativeAot })
        {
            await Connection(variant).OpenAsync();
            await using var context = Built(variant);
            await context.Database.EnsureCreatedAsync();
            await SeedAsync(context);
        }
    }

    public Task DisposeAsync()
        => Task.CompletedTask;

    public void Dispose()
    {
        _sqlite.Dispose();
        _sqliteNativeAot.Dispose();
    }

    [Theory]
    [MemberData(nameof(OnSqlite))]
    public void CompiledModel_ShouldConvertEveryPropertyAsTheBuiltModel(string variant)
    {
        using var built = Built(variant);
        using var compiled = Compiled(variant);

        Converters(compiled.Model).ShouldBe(Converters(built.Model));
        Converters(compiled.Model).ShouldContain("VoPlayer.Id: PlayerId -> String");
    }

    [Fact]
    public void CompiledModel_ShouldUseTheConvertersClass_WhenNotForNativeAot()
    {
        using var compiled = Compiled(Sqlite);
        var player = compiled.Model.FindEntityType(typeof(VoPlayer))!;

        player.FindProperty(nameof(VoPlayer.Id))!.GetTypeMapping().Converter
            .ShouldBeOfType<SingleValueObjectConverter<PlayerId, string>>();
        player.FindComplexProperty(nameof(VoPlayer.Stats))!.ComplexType.FindProperty(nameof(VoStats.Referrer))!
            .GetTypeMapping().Converter.ShouldBeOfType<SingleValueObjectConverter<AllyCode, long>>();
    }

    [Fact]
    public void Generate_ShouldCreateTheConverter_WithItsParameterlessConstructor()
    {
        var code = GeneratedCode(Sqlite);

        code.ShouldContain("valueConverter: new SingleValueObjectConverter<PlayerId, string>()");
    }

    [Fact]
    public void Generate_ShouldWriteTheConversionAsPublicCalls_ForNativeAot()
    {
        var code = GeneratedCode(SqliteNativeAot);

        code.ShouldContain("((ISingleValueObject<PlayerId, string>)valueObject).Value");
        code.ShouldContain("SingleValueObjectConverter.FromPersisted<PlayerId, string>(value)");
    }

    [Theory]
    [MemberData(nameof(Queries))]
    public void CompiledModel_ShouldTranslateQueries_ToTheSameSql(string query)
    {
        foreach (var variant in Supporting(query, Sqlite, SqliteNativeAot, Npgsql))
        {
            using var built = Built(variant);
            using var compiled = Compiled(variant);

            PlayerQueries[query](compiled).ToQueryString()
                .ShouldBe(PlayerQueries[query](built).ToQueryString(), variant);
        }
    }

    [Theory]
    [MemberData(nameof(Queries))]
    public async Task CompiledModel_ShouldReturnTheSameRows_AsTheBuiltModel(string query)
    {
        foreach (var variant in Supporting(query, Sqlite, SqliteNativeAot))
        {
            await using var built = Built(variant);
            await using var compiled = Compiled(variant);

            var expected = Describe(await PlayerQueries[query](built).ToListAsync());
            var actual = Describe(await PlayerQueries[query](compiled).ToListAsync());

            actual.ShouldBe(expected, variant);
            expected.ShouldNotBeEmpty(variant);
        }
    }

    [Theory]
    [MemberData(nameof(OnSqlite))]
    public async Task CompiledModel_ShouldFindByKey_AsTheBuiltModel(string variant)
    {
        await using var built = Built(variant);
        await using var compiled = Compiled(variant);

        var expected = await built.Players.FindAsync(Second);
        var actual = await compiled.Players.FindAsync(Second);

        Describe([actual!]).ShouldBe(Describe([expected!]));
    }

    [Theory]
    [MemberData(nameof(OnSqlite))]
    public async Task CompiledModel_ShouldWriteWhatTheBuiltModelReads_AndBack(string variant)
    {
        await using (var compiled = Compiled(variant))
        {
            var third = new VoPlayer(PlayerId.Create("P3").Value, AllyCode.Create(444_444_444).Value)
            {
                SimulatedPlayerId = First,
                FormerAllyCode = FormerAllyCode,
                Code = TrustedCode.Create("XYZ").Value,
                Stats = new VoStats { Level = 3, Referrer = FirstAllyCode },
                Friends = [First, Second],
            };
            third.Memberships.Add(new VoMembership(
                OrderNumber.Create(9).Value,
                third.Id,
                TrackingId.Create(Guid.NewGuid()).Value));
            compiled.Players.Add(third);
            compiled.Tickets.Add(new VoTicket(OrderNumber.Create(8).Value, TrackingId.Create(Guid.NewGuid()).Value));
            await compiled.SaveChangesAsync();
        }

        await using var built = Built(variant);
        await using var readBack = Compiled(variant);
        var written = await built.Players.Include(player => player.Memberships).OrderBy(player => player.Id).ToListAsync();
        var read = await readBack.Players.Include(player => player.Memberships).OrderBy(player => player.Id).ToListAsync();

        Describe(read).ShouldBe(Describe(written));
        written.Count.ShouldBe(3);
        (await built.Tickets.CountAsync()).ShouldBe(2);
    }

    [Theory]
    [MemberData(nameof(OnSqlite))]
    public async Task CompiledModel_ShouldTrackChanges_AsTheBuiltModel(string variant)
    {
        await using var context = Compiled(variant);
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

    [Theory]
    [MemberData(nameof(OnSqlite))]
    public async Task CompiledModel_ShouldThrowOnAStoredValueThatNoLongerValidates_AsTheBuiltModel(string variant)
    {
        await using (var context = Built(variant))
            await context.Database.ExecuteSqlRawAsync("UPDATE \"Players\" SET \"AllyCode\" = 5 WHERE \"Id\" = 'P1'");

        await using var built = Built(variant);
        await using var compiled = Compiled(variant);

        var expected = await Should.ThrowAsync<InvalidOperationException>(() => built.Players.ToListAsync());
        var actual = await Should.ThrowAsync<InvalidOperationException>(() => compiled.Players.ToListAsync());

        actual.Message.ShouldBe(expected.Message);
    }

    [Theory]
    [MemberData(nameof(OnSqlite))]
    public async Task CompiledModel_ShouldReadThroughFromPersisted_AsTheBuiltModel(string variant)
    {
        await using (var context = Built(variant))
            await context.Database.ExecuteSqlRawAsync("UPDATE \"Players\" SET \"Code\" = 'TOOLONG' WHERE \"Id\" = 'P1'");

        await using var compiled = Compiled(variant);

        var player = await compiled.Players.SingleAsync(player => player.Id == First);
        player.Code!.Value.ShouldBe("TOOLONG");
    }

    // The NativeAOT model has no owned type (see NativeAotConventionDbContext), so no query on it.
    private static string[] Supporting(
        string query,
        params string[] variants)
        => query == "owned"
            ? [.. variants.Where(variant => variant != SqliteNativeAot)]
            : variants;

    /// <summary>The context with the model it builds at run time.</summary>
    private ConventionDbContext Built(string variant)
        => Create(variant, model: null);

    /// <summary>The same context on the compiled model.</summary>
    private ConventionDbContext Compiled(string variant)
        => Create(variant, Model(variant));

    private ConventionDbContext Create(
        string variant,
        IModel? model)
    {
        var options = new DbContextOptionsBuilder<ConventionDbContext>();
        if (variant == Npgsql)
            options.UseNpgsql(ModelOnlyNpgsql);
        else
            options.UseSqlite(Connection(variant));
        if (model is not null)
            options.UseModel(model);

        return variant == SqliteNativeAot
            ? new NativeAotConventionDbContext(options.Options)
            : new ConventionDbContext(options.Options);
    }

    private SqliteConnection Connection(string variant)
        => variant == SqliteNativeAot ? _sqliteNativeAot : _sqlite;

    private static IModel Model(string variant)
        => Models
            .GetOrAdd(variant, key => new Lazy<IModel>(() => Compile(key)))
            .Value;

    // Generated from the model the context builds on the provider, as `dotnet ef dbcontext optimize` does.
    private static IModel Compile(string variant)
    {
        using var context = ModelOnly(variant);
        return CompiledModels.CompileAndLoad(
            CompiledModels.Generate(context, forNativeAot: variant == SqliteNativeAot),
            $"{CompiledModels.Namespace}.{variant}",
            context.GetType());
    }

    private static string GeneratedCode(string variant)
    {
        using var context = ModelOnly(variant);
        return string.Join(
            Environment.NewLine,
            CompiledModels
                .Generate(context, forNativeAot: variant == SqliteNativeAot)
                .Select(file => file.Code));
    }

    private static ConventionDbContext ModelOnly(string variant)
    {
        var options = new DbContextOptionsBuilder<ConventionDbContext>();
        if (variant == Npgsql)
            options.UseNpgsql(ModelOnlyNpgsql);
        else
            options.UseSqlite("DataSource=:memory:");

        return variant == SqliteNativeAot
            ? new NativeAotConventionDbContext(options.Options)
            : new ConventionDbContext(options.Options);
    }

    // Per property, what its converter converts. The NativeAOT model writes each converter out as a ValueConverter
    // (the conversion's code), so the class is compared only where it stays (CompiledModel_ShouldUseTheConvertersClass).
    private static string[] Converters(IModel model)
        => [.. model
            .GetEntityTypes()
            .SelectMany(entityType => entityType.GetFlattenedProperties())
            .Select(property => $"{property.DeclaringType.ClrType.Name}.{property.Name}: " + Conversion(property))
            .Order(StringComparer.Ordinal)];

    private static string Conversion(IProperty property)
        => property.GetTypeMapping().Converter is { } converter
            ? $"{converter.ModelClrType.Name} -> {converter.ProviderClrType.Name}"
            : "none";
}
