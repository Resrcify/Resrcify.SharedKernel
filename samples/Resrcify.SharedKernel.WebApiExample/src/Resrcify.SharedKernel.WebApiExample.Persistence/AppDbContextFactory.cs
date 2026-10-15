using Resrcify.SharedKernel.UnitOfWork.Postgres.Design;

namespace Resrcify.SharedKernel.WebApiExample.Persistence;

/// <summary>For <c>dotnet ef</c>: the Database section of the Web project's appsettings, as the host reads it.</summary>
public sealed class AppDbContextFactory
    : PostgresDesignTimeFactory<AppDbContext>;
