using System;
using System.Collections.Generic;
using System.IO;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.Design;

/// <summary>Finds the settings a design-time factory reads: the Web project's <c>appsettings.json</c>.</summary>
internal static class DesignTimeSettings
{
    private const string SettingsFile = "appsettings.json";
    private const string PersistenceSuffix = ".Persistence";

    /// <summary>
    /// The Web project next to a Persistence project: <c>Titan.Shard.Persistence</c> → <c>Titan.Shard.Web</c>;
    /// <see langword="null"/> for an assembly not named <c>*.Persistence</c>.
    /// </summary>
    public static string? WebProjectFor(string? assemblyName)
    {
        if (assemblyName is null || !assemblyName.EndsWith(PersistenceSuffix, StringComparison.Ordinal))
            return null;

        return string.Concat(assemblyName.AsSpan(0, assemblyName.Length - PersistenceSuffix.Length), ".Web");
    }

    /// <summary>
    /// The directory holding the settings: a start directory that has an <c>appsettings.json</c> itself (the startup
    /// project is the Web project), else the first <c>&lt;web project&gt;</c> or <c>src/&lt;web project&gt;</c> with one,
    /// looking up from each start directory (<c>dotnet ef</c>'s working directory, the startup project's output).
    /// </summary>
    public static string? Find(
        string? webProjectName,
        IEnumerable<string> startDirectories)
    {
        var starts = new List<DirectoryInfo>();
        foreach (var start in startDirectories)
        {
            if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start))
                continue;
            if (HasSettings(start))
                return Path.GetFullPath(start);
            starts.Add(new DirectoryInfo(start));
        }

        if (webProjectName is null)
            return null;

        foreach (var start in starts)
        {
            for (var directory = start; directory is not null; directory = directory.Parent)
            {
                if (WebProjectIn(directory.FullName, webProjectName) is { } found)
                    return found;
            }
        }

        return null;
    }

    private static string? WebProjectIn(
        string directory,
        string webProjectName)
    {
        var sibling = Path.Combine(directory, webProjectName);
        if (HasSettings(sibling))
            return sibling;

        var underSrc = Path.Combine(directory, "src", webProjectName);
        return HasSettings(underSrc) ? underSrc : null;
    }

    private static bool HasSettings(string directory)
        => File.Exists(Path.Combine(directory, SettingsFile));
}
