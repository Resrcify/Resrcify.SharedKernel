using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.Configuration;

/// <summary>Checks one DbContext's <see cref="PostgresOptions"/> (its named options), naming the keys that are wrong.</summary>
/// <param name="optionsName">The named options this validates (the DbContext's).</param>
/// <param name="sectionPath">The section they are bound from, for the messages (e.g. <c>Database</c>).</param>
internal sealed class PostgresOptionsValidator(
    string optionsName,
    string sectionPath)
    : IValidateOptions<PostgresOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        PostgresOptions options)
    {
        if (name != optionsName)
            return ValidateOptionsResult.Skip;

        var failures = new List<string>();
        Require(failures, nameof(PostgresOptions.Host), options.Host);
        Require(failures, nameof(PostgresOptions.Database), options.Database);
        Require(failures, nameof(PostgresOptions.Username), options.Username);
        Require(failures, nameof(PostgresOptions.Password), options.Password);
        if (options.Port is < 1 or > 65535)
            failures.Add($"{sectionPath}:{nameof(PostgresOptions.Port)} must be a port (1-65535); it is {options.Port}.");
        if (options.CommandTimeoutInSeconds < 0)
            failures.Add($"{sectionPath}:{nameof(PostgresOptions.CommandTimeoutInSeconds)} can't be negative.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private void Require(
        List<string> failures,
        string key,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            failures.Add($"{sectionPath}:{key} is required.");
    }
}
