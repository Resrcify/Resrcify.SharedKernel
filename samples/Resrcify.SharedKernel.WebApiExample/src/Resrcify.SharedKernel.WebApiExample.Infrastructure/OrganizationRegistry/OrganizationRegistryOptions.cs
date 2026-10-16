using System;
using System.ComponentModel.DataAnnotations;

namespace Resrcify.SharedKernel.WebApiExample.Infrastructure.OrganizationRegistry;

/// <summary>Where the organization registry is (<c>OrganizationRegistry</c>).</summary>
public sealed class OrganizationRegistryOptions
{
    public const string SectionName = "OrganizationRegistry";

    [Required]
    public Uri Url { get; set; } = default!;
}
