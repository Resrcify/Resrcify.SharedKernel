using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Resrcify.SharedKernel.ArchitectureTesting.UnitTests.Tests;

/// <summary>
/// The types <c>Types_Should_NotTakeIConfigurationInAConstructor</c> is tested on: the first group takes configuration
/// in a constructor (each must be named), the second reads it where it belongs or not at all (none may be named).
/// </summary>
internal static class SampleConfigurationReaders
{
    internal sealed record SampleOptions(string Url);

    // ───────────────────── Constructors that take configuration ─────────────────────

    internal sealed class ConfigurationReadingService(
        IConfiguration configuration)
    {
        public string? Url
            => configuration["Sample:Url"];
    }

    internal sealed class SectionReadingService
    {
        private readonly IConfigurationSection _section;

        public SectionReadingService(IConfigurationSection section)
            => _section = section;

        public string? Url
            => _section["Url"];
    }

    internal sealed class PrivatelyConstructedService
    {
        private readonly IConfiguration _configuration;

        private PrivatelyConstructedService(IConfiguration configuration)
            => _configuration = configuration;

        public static PrivatelyConstructedService From(IConfiguration configuration)
            => new(configuration);

        public string? Url
            => _configuration["Sample:Url"];
    }

    // ───────────────────── Configuration where it belongs ─────────────────────

    internal sealed class OptionsReadingService(
        IOptions<SampleOptions> options)
    {
        public string Url
            => options.Value.Url;
    }

    internal static IServiceCollection AddSample(
        this IServiceCollection services,
        IConfiguration configuration)
        => services.AddSingleton(new SampleOptions(configuration["Sample:Url"] ?? string.Empty));
}
