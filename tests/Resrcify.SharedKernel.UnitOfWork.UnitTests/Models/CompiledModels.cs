using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.Extensions.DependencyInjection;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;

/// <summary>
/// What <c>dotnet ef dbcontext optimize</c> does, in the test: the compiled model's C# from EF Core's design-time
/// <see cref="ICompiledModelCodeGenerator"/> (the generator <c>optimize</c> calls, on the same design-time model), compiled
/// with Roslyn into an assembly of its own and loaded, so a context can <c>UseModel(...Model.Instance)</c>.
/// </summary>
internal static class CompiledModels
{
    /// <summary>The namespace the generated model is put in.</summary>
    public const string Namespace = "Resrcify.SharedKernel.UnitOfWork.UnitTests.CompiledModels";

    /// <summary>
    /// The generated files for <paramref name="context"/>'s model. <paramref name="forNativeAot"/> is what
    /// <c>optimize --native-aot</c> (and <c>--precompile-queries</c>) generates: every type mapping and converter
    /// written out as code instead of being built at run time.
    /// </summary>
    public static IReadOnlyCollection<ScaffoldedFile> Generate(
        DbContext context,
        bool forNativeAot = false)
    {
        using var services = DesignTimeServices.For(context);
        var options = new CompiledModelCodeGenerationOptions
        {
            ContextType = context.GetType(),
            ModelNamespace = Namespace,
            Language = "C#",
            UseNullableReferenceTypes = true,
            ForNativeAot = forNativeAot,
        };

        return services
            .GetRequiredService<ICompiledModelCodeGeneratorSelector>()
            .Select(options)
            .GenerateModel(context.GetService<IDesignTimeModel>().Model, options);
    }

    /// <summary>
    /// Compiles <paramref name="files"/> into an assembly named <paramref name="assemblyName"/> (which the test assembly
    /// lets see its internal types), loads it, and returns the model's <c>Instance</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The generated code doesn't compile; the message has the errors.</exception>
    public static IModel CompileAndLoad(
        IReadOnlyCollection<ScaffoldedFile> files,
        string assemblyName,
        Type contextType)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            files.Select(file => CSharpSyntaxTree.ParseText(
                ForInternalEntityTypes(file),
                new CSharpParseOptions(LanguageVersion.Latest),
                path: file.Path)),
            References(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        if (!emitted.Success)
            throw new InvalidOperationException(
                "The compiled model doesn't compile:" + Environment.NewLine + string.Join(
                    Environment.NewLine,
                    emitted.Diagnostics
                        .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                        .Select(diagnostic => diagnostic.ToString())));

        image.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
        return Instance(assembly, contextType);
    }

    /// <summary>
    /// The file's code, with the classes of <c>[UnsafeAccessor]</c> methods made internal. EF Core writes them public
    /// (for private setters and backing fields, in the NativeAOT model), which can't compile against internal entity
    /// types like this test's; a service's entities are public. Nothing else is changed.
    /// </summary>
    private static string ForInternalEntityTypes(ScaffoldedFile file)
        => file.Path.EndsWith(UnsafeAccessorsFileSuffix, StringComparison.Ordinal)
            ? file.Code.Replace("public static class ", "internal static class ", StringComparison.Ordinal)
            : file.Code;

    private const string UnsafeAccessorsFileSuffix = "UnsafeAccessors.cs";

    private static IModel Instance(
        Assembly assembly,
        Type contextType)
    {
        var modelType = assembly
            .GetTypes()
            .Single(type => type.GetCustomAttribute<DbContextAttribute>()?.ContextType == contextType);
        return (IModel)modelType
            .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;
    }

    // The framework, and every assembly the test runs with (EF Core, the providers, this one, the SharedKernel).
    private static List<MetadataReference> References()
    {
        var platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var loaded = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => assembly.Location);

        return platform
            .Concat(loaded)
            .DistinctBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
    }
}
