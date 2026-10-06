using System;
using Microsoft.Extensions.DependencyInjection;

namespace Resrcify.SharedKernel.Mediator.Configuration;

public readonly record struct OpenBehaviorRegistration(
    Type BehaviorType,
    ServiceLifetime Lifetime);
