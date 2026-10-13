using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;

namespace Resrcify.SharedKernel.Web.Health;

/// <summary>Several endpoints given the same conventions, as one builder.</summary>
internal sealed class EndpointConventionBuilders(
    IReadOnlyList<IEndpointConventionBuilder> builders)
    : IEndpointConventionBuilder
{
    public void Add(
        Action<EndpointBuilder> convention)
    {
        foreach (var builder in builders)
            builder.Add(convention);
    }

    public void Finally(
        Action<EndpointBuilder> finallyConvention)
    {
        foreach (var builder in builders)
            builder.Finally(finallyConvention);
    }
}
