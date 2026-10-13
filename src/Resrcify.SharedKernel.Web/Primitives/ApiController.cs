using Microsoft.AspNetCore.Mvc;
using Resrcify.SharedKernel.Abstractions.Mediator;

namespace Resrcify.SharedKernel.Web.Primitives;

[ApiController]
public abstract class ApiController : ControllerBase
{
    protected ISender Sender { get; }

    protected ApiController(ISender sender)
        => Sender = sender;
}