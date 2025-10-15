using Microsoft.AspNetCore.Routing;

namespace Resrcify.SharedKernel.Web.Abstractions;

public interface IEndpoint
{
    void MapEndpoint(
        IEndpointRouteBuilder app);
}
