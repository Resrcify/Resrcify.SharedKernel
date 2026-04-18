using Microsoft.AspNetCore.Routing;

namespace Resrcify.SharedKernel.Abstractions.Web;

public interface IEndpoint
{
    void MapEndpoint(
        IEndpointRouteBuilder app);
}
