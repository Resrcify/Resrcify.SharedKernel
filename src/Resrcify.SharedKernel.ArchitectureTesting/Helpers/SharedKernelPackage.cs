namespace Resrcify.SharedKernel.ArchitectureTesting.Helpers;

/// <summary>
/// The SharedKernel packages whose use is restricted to some layers. Contracts (<c>Abstractions</c>), <c>Results</c>
/// and <c>DomainDrivenDesign</c> are allowed everywhere, so they aren't listed. Each package's root namespace is
/// <c>Resrcify.SharedKernel.&lt;name&gt;</c> (see <see cref="SharedKernelPackageExtensions.Namespace"/>).
/// </summary>
public enum SharedKernelPackage
{
    Mediator,
    MessageBus,
    UnitOfWork,

    /// <summary><c>Resrcify.SharedKernel.UnitOfWork.Postgres</c>: the PostgreSQL DbContext setup.</summary>
    UnitOfWorkPostgres,
    Repository,
    Caching,
    Web,
    Observability,
    ArchitectureTesting,
    IntegrationTesting,
}

/// <summary>Maps a <see cref="SharedKernelPackage"/> to the namespace its types live in.</summary>
public static class SharedKernelPackageExtensions
{
    /// <summary>The package's root namespace, e.g. <c>Resrcify.SharedKernel.MessageBus</c>.</summary>
    public static string Namespace(this SharedKernelPackage package)
        => package switch
        {
            SharedKernelPackage.UnitOfWorkPostgres => "Resrcify.SharedKernel.UnitOfWork.Postgres",
            _ => $"Resrcify.SharedKernel.{package}",
        };
}
