using System;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Resrcify.SharedKernel.Abstractions.Mediator;

namespace Resrcify.SharedKernel.Mediator.Behaviors;

/// <summary>
/// What an idempotent request asks for: a hash of its JSON without its key and scope, so a key repeated with a different
/// request can be told apart.
/// </summary>
internal static class IdempotencyFingerprint
{
    private static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { WithoutKeyAndScope } },
    };

    /// <summary>The hash of <paramref name="request"/>'s JSON; its type's name when it can't be written as JSON.</summary>
    public static string Of(IIdempotentRequest request)
    {
        byte[] json;
        try
        {
            json = JsonSerializer.SerializeToUtf8Bytes(request, request.GetType(), Options);
        }
        catch (Exception exception) when (exception is NotSupportedException or JsonException or InvalidOperationException)
        {
            // Not writable as JSON (a stream, a delegate): a repeated key can't be checked against the request.
            return request.GetType().FullName ?? request.GetType().Name;
        }

        return Convert.ToHexString(SHA256.HashData(json));
    }

    private static void WithoutKeyAndScope(JsonTypeInfo type)
    {
        for (var index = type.Properties.Count - 1; index >= 0; index--)
            if (type.Properties[index].AttributeProvider is PropertyInfo
                {
                    Name: nameof(IIdempotentRequest.IdempotencyKey) or nameof(IIdempotentRequest.IdempotencyScope),
                })
                type.Properties.RemoveAt(index);
    }
}
