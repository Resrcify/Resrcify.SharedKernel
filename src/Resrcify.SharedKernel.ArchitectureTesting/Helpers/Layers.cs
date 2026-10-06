namespace Resrcify.SharedKernel.ArchitectureTesting.Helpers;

/// <summary>
/// The conventional layer names (the suffix of each layer's assembly, e.g. <c>Titan.Shard.Application</c>). A service
/// can add its own (Bot, Client, …) as plain strings; these are the ones the conventional tests know.
/// </summary>
public static class Layers
{
    public const string Domain = nameof(Domain);
    public const string Application = nameof(Application);
    public const string Persistence = nameof(Persistence);
    public const string Infrastructure = nameof(Infrastructure);
    public const string Presentation = nameof(Presentation);
    public const string Web = nameof(Web);
}
