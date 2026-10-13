using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Resrcify.SharedKernel.UnitOfWork.Postgres.Design;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.UnitTests.Design;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class DesignTimeSettingsTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"designtime-{Guid.NewGuid():N}")).FullName;

    [Theory]
    [InlineData("Titan.ShardManagement.Persistence", "Titan.ShardManagement.Web")]
    [InlineData("Resrcify.Sandbox.Persistence", "Resrcify.Sandbox.Web")]
    [InlineData("Titan.ShardManagement.Infrastructure", null)]
    [InlineData(null, null)]
    public void WebProjectFor_ShouldNameTheWebProjectNextToAPersistenceProject(
        string? assemblyName,
        string? expected)
        => DesignTimeSettings.WebProjectFor(assemblyName).ShouldBe(expected);

    [Fact]
    public void Find_ShouldFindTheWebProjectUnderSrc_FromTheBuildOutput()
    {
        var web = Folder("src/Titan.Shard.Web", withSettings: true);
        var output = Folder("src/Titan.Shard.Persistence/bin/Debug/net10.0");

        DesignTimeSettings.Find("Titan.Shard.Web", [output]).ShouldBe(web);
    }

    [Fact]
    public void Find_ShouldFindASiblingWebProject_FromThePersistenceProject()
    {
        var web = Folder("Titan.Shard.Web", withSettings: true);
        var persistence = Folder("Titan.Shard.Persistence");

        DesignTimeSettings.Find("Titan.Shard.Web", [persistence]).ShouldBe(web);
    }

    [Fact]
    public void Find_ShouldUseAStartDirectoryWithSettings_WhenTheStartupProjectIsTheWebProject()
    {
        var web = Folder("src/Titan.Shard.Web", withSettings: true);

        DesignTimeSettings.Find(webProjectName: null, [web]).ShouldBe(web);
    }

    [Fact]
    public void Find_ShouldReturnNull_WhenThereIsNoWebProject()
        => DesignTimeSettings.Find("Titan.Shard.Web", [Folder("src/Titan.Shard.Persistence")]).ShouldBeNull();

    public void Dispose()
        => Directory.Delete(_root, recursive: true);

    private string Folder(
        string relativePath,
        bool withSettings = false)
    {
        var directory = Directory.CreateDirectory(Path.GetFullPath(Path.Combine(_root, relativePath))).FullName;
        if (withSettings)
            File.WriteAllText(Path.Combine(directory, "appsettings.json"), "{}");
        return directory;
    }
}
