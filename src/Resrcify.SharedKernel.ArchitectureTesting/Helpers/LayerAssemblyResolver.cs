using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Resrcify.SharedKernel.ArchitectureTesting.Helpers;

/// <summary>
/// Resolves the consumer's layer assemblies by naming convention. Given a test
/// assembly named <c>Foo.Bar.ArchitectureTests</c>, derives the root prefix
/// <c>Foo.Bar</c> and discovers any referenced assembly named
/// <c>Foo.Bar.&lt;Layer&gt;</c>.
///
/// Layer names are not hard-coded — whatever suffixes the consumer uses (Domain,
/// Application, Infrastructure, Presentation, Persistence, Web, Bot,
/// SourceGenerators, Client, …) are discovered automatically. Each unique
/// suffix is exposed as a key in the resulting dictionary.
///
/// Results are cached per resolving assembly so consumers resolve once per
/// test run regardless of how many test classes inherit a base.
/// </summary>
public static class LayerAssemblyResolver
{
    private static readonly ConcurrentDictionary<Assembly, IReadOnlyDictionary<string, Assembly>> Cache = new();

    public static IReadOnlyDictionary<string, Assembly> Resolve(Assembly testAssembly)
        => Cache.GetOrAdd(testAssembly, ResolveCore);

    private static IReadOnlyDictionary<string, Assembly> ResolveCore(Assembly testAssembly)
    {
        var rootPrefix = GetRootNamespacePrefix(testAssembly);
        EnsureReferencedLayersLoaded(testAssembly, rootPrefix);

        var prefixWithDot = rootPrefix + ".";

        return AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.GetName().Name))
            .Where(a => a.GetName().Name!.StartsWith(prefixWithDot, StringComparison.Ordinal))
            .Where(a => !a.GetName().Name!.Contains("ArchitectureTests", StringComparison.Ordinal))
            .Where(a => !a.GetName().Name!.Contains("UnitTests", StringComparison.Ordinal))
            .Where(a => !a.GetName().Name!.Contains("IntegrationTests", StringComparison.Ordinal))
            .GroupBy(a => ExtractLayerName(a.GetName().Name!, prefixWithDot))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    private static string ExtractLayerName(string assemblyName, string prefixWithDot)
    {
        var suffix = assemblyName[prefixWithDot.Length..];
        var firstDot = suffix.IndexOf('.', StringComparison.Ordinal);
        return firstDot < 0 ? suffix : suffix[..firstDot];
    }

    private static string GetRootNamespacePrefix(Assembly testAssembly)
    {
        var name = testAssembly.GetName().Name!;
        var parts = name.Split('.');
        return parts.Length < 2
            ? name
            : string.Join('.', parts.Take(parts.Length - 1));
    }

    private static void EnsureReferencedLayersLoaded(Assembly testAssembly, string rootPrefix)
    {
        var loaded = new HashSet<string>(
            AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name!),
            StringComparer.Ordinal);

        var prefixWithDot = rootPrefix + ".";

        // 1. Statically-referenced assemblies (those the test code actually uses).
        foreach (var refName in testAssembly.GetReferencedAssemblies())
        {
            if (refName.Name?.StartsWith(prefixWithDot, StringComparison.Ordinal) != true)
                continue;
            if (loaded.Contains(refName.Name))
                continue;

            try
            {
                Assembly.Load(refName);
                loaded.Add(refName.Name);
            }
            catch
            {
                // Best-effort.
            }
        }

        // 2. Bin-directory scan: catches layer assemblies referenced via csproj
        //    ProjectReference but not actually used by test code (e.g. when the
        //    consumer just inherits Conventional*Tests and writes no rule code,
        //    the compiler optimises away the reference). dotnet test copies all
        //    transitive deps into the test bin folder, so we scan that folder
        //    for any DLL matching the consumer's <RootPrefix>.<Layer> pattern.
        var location = testAssembly.Location;
        if (string.IsNullOrEmpty(location))
            return;

        var binDir = Path.GetDirectoryName(location);
        if (string.IsNullOrEmpty(binDir) || !Directory.Exists(binDir))
            return;

        foreach (var dll in Directory.EnumerateFiles(binDir, $"{rootPrefix}.*.dll", SearchOption.TopDirectoryOnly))
        {
            var assemblyName = Path.GetFileNameWithoutExtension(dll);
            if (loaded.Contains(assemblyName))
                continue;

            try
            {
#pragma warning disable S3885 // We genuinely need LoadFrom — these DLLs aren't on the probing path.
                Assembly.LoadFrom(dll);
#pragma warning restore S3885
                loaded.Add(assemblyName);
            }
            catch
            {
                // Best-effort.
            }
        }
    }
}
