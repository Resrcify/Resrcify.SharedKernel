using System;
using Microsoft.Extensions.DependencyInjection;

namespace Resrcify.SharedKernel.Messaging.Configuration;

public readonly record struct OpenBehaviorRegistration(
    Type BehaviorType,
    ServiceLifetime Lifetime);
