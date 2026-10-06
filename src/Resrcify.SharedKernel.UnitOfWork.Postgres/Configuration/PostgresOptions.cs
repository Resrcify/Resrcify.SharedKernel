namespace Resrcify.SharedKernel.UnitOfWork.Postgres.Configuration;

/// <summary>
/// Where a DbContext's PostgreSQL database is: the <c>Database</c> section every service already has
/// (<c>Host</c>, <c>Port</c>, <c>Database</c>, <c>Username</c>, <c>Password</c>), so a service switches to
/// <c>AddPostgresDbContext</c> without a configuration change. Validated when the host starts.
/// </summary>
/// <remarks>
/// The connection string is built with <c>NpgsqlConnectionStringBuilder</c>, so a password with a <c>;</c>, a quote or
/// a space is passed as it is (an interpolated <c>password=...;</c> breaks on those).
/// </remarks>
public sealed class PostgresOptions
{
    /// <summary>The section the services keep their database settings in.</summary>
    public const string SectionName = "Database";

    /// <summary>The server's host name or address. Required.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>The server's port; 5432 by default. A string in configuration (<c>"5432"</c>) binds too.</summary>
    public int Port { get; set; } = 5432;

    /// <summary>The database's name. Required.</summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>The user to connect as. Required.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>The user's password. Required.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Seconds a command may run before it is cancelled; Npgsql's default (30) when not set, 0 for no limit. Sentinel
    /// and Tournament set it in their connection strings (500, 300); with this they set it here.
    /// </summary>
    public int? CommandTimeoutInSeconds { get; set; }
}
