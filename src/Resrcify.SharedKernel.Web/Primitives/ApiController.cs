using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Resrcify.SharedKernel.Web.Primitives;

[ApiController]
public abstract class ApiController : ControllerBase
{
    protected ISender Sender { get; }

    protected ApiController(ISender sender)
        => Sender = sender;
}