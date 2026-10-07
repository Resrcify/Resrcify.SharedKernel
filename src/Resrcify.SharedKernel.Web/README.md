# Resrcify.SharedKernel.Web

`Resrcify.SharedKernel.Web` provides helpers for converting `Result`/`Result<T>` outcomes into HTTP responses and standardized problem details.

## Table of Contents

- [Resrcify.SharedKernel.Web](#resrcifysharedkernelweb)
  - [Table of Contents](#table-of-contents)
  - [What you get](#what-you-get)
  - [Prerequisites](#prerequisites)
  - [Install](#install)
    - [Option A: Project reference](#option-a-project-reference)
    - [Option B: NuGet package](#option-b-nuget-package)
  - [Quick Start](#quick-start)
  - [Usage guide](#usage-guide)
    - [Convert result to problem details](#convert-result-to-problem-details)
    - [Use Match in controllers](#use-match-in-controllers)
    - [Functional endpoint flow](#functional-endpoint-flow)
    - [Read an HTTP response as a result](#read-an-http-response-as-a-result)
    - [Unhandled exceptions: the same problem details](#unhandled-exceptions-the-same-problem-details)
    - [Health endpoints](#health-endpoints)
    - [Calling another service: resilience keyed on ErrorType](#calling-another-service-resilience-keyed-on-errortype)
    - [Accepting Resrcify.Identity's tokens](#accepting-resrcifyidentitys-tokens)
  - [Common issues](#common-issues)
  - [Sample project](#sample-project)

## What you get

- `ApiController` base type in `Primitives/`.
- Result-to-HTTP conversion extensions in `Extensions/`.
- Consistent mapping from `ErrorType` to HTTP problem responses.
- `AddResultProblemDetails()`: unhandled exceptions answered with the same problem details as a failed result.
- `MapHealthEndpoints()`: `/health`, `/health/ready` and `/health/live`, anonymous, with a JSON response writer.
- `AddResultResilience()`: retries, timeouts and a circuit breaker for an `HttpClient`, retrying what the result
  pattern calls transient.
- `AddResrcifyJwtBearer(configuration)`: JWT bearer authentication against Resrcify.Identity.

## Prerequisites

- .NET 10 SDK.
- ASP.NET Core dependencies in the consuming application.
- `Resrcify.SharedKernel.Results` and `Resrcify.SharedKernel.Mediator` referenced by the consuming application.

## Install

### Option A: Project reference

```xml
<ProjectReference Include="..\path\to\Resrcify.SharedKernel.Web.csproj" />
```

### Option B: NuGet package

```xml
<PackageReference Include="Resrcify.SharedKernel.Web" Version="<latest>" />
```

CLI:

```powershell
dotnet add package Resrcify.SharedKernel.Web
```

## Quick Start

```csharp
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;

services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
```

## Usage guide

### Convert result to problem details

```csharp
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.Web.Extensions;

Result result = await SomeOperationAsync();

if (result.IsFailure)
{
    return result.ToProblemDetails();
}

return Results.Ok();
```

### Use Match in controllers

```csharp
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.Web.Primitives;

[Route("api/company")]
public sealed class CompanyController(
    ISender sender)
    : ApiController(sender)
{
    [HttpGet("{id:guid}")]
    public async Task<IResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        Result<CompanyDto> result = await Sender.Send(new GetCompanyByIdQuery(id), cancellationToken);

        return result.Match(
            onSuccess: Results.Ok,
            onFailure: ToProblemDetails);
    }
}
```

### Functional endpoint flow

```csharp
return await Result
    .Create(new GetCompanyByIdQuery(id))
    .Bind(request => Sender.Send(request, cancellationToken))
    .Match(
        onSuccess: Results.Ok,
        onFailure: ToProblemDetails);
```

### Read an HTTP response as a result

`HttpResponseMessageExtensions.ToResultAsync` reads a response from another service:

```csharp
using var response = await httpClient.GetAsync($"players/{id}", cancellationToken);
Result<PlayerDto> player = await response.ToResultAsync<PlayerDto>(cancellationToken: cancellationToken);
```

- A success body is deserialized; a 204 or a `null` body gives `Http.EmptyContent`, a body that isn't JSON gives
  `Http.UnreadableContent`.
- A failure gives the errors in its problem details, or one `Http.<status>` error of the status's type when the body
  isn't problem details (an HTML page from a proxy, an empty body).

The two directions live in two classes: `HttpResultExtensions` (`ToProblemDetails`, `Match`: a result to an HTTP
response) and `HttpResponseMessageExtensions` (`ToResultAsync`: an HTTP response to a result). Neither clashes with
`Resrcify.SharedKernel.Results.Primitives.ResultExtensions`, so endpoint files need no alias; a method group is
`HttpResultExtensions.ToProblemDetails`.

### Unhandled exceptions: the same problem details

```csharp
builder.Services.AddResultProblemDetails();
// ...
app.UseExceptionHandler();
```

An exception no handler caught is answered like a failed result (`ToProblemDetails`): a 500 problem details whose
`errors` hold one `Failure` error coded `Unhandled` (`ResultExceptionHandler.ErrorCode`), so a caller's
`ToResultAsync` reads it back as that error. Outside Development the message is only "An unexpected error occurred.";
in Development the error's message is the exception's type and message and the problem's `detail` the whole exception
(`options.IncludeExceptionDetails` decides either way). The exception is logged once, at `Error` (the exception
handler middleware logs nothing more for an exception a handler handled), following Results' `LoggedExceptions`
convention: when the mediator's behaviors logged it already on its way out, the handler logs a `Debug` line without the
stack instead, and answers the same 500. A request the client aborted (an
`OperationCanceledException` once `RequestAborted` fired) is a 499, logged at `Debug`, not as an error.

It replaces `builder.Services.AddProblemDetails()` (it registers it); keep `app.UseExceptionHandler()`.

### Health endpoints

```csharp
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres", tags: [HealthTags.Ready])
    .AddCheck<QuartzJobLivenessHealthCheck>("quartz-jobs", tags: [HealthTags.Live]);
// ...
app.MapHealthEndpoints();
```

| Path | Runs | |
|---|---|---|
| `/health` | every check | |
| `/health/ready` | the checks tagged `HealthTags.Ready` (`"ready"`) | the readiness probe |
| `/health/live` | the checks tagged `HealthTags.Live` (`"live"`) | the liveness probe: Healthy without any |

All three are anonymous and answer 200 when Healthy or Degraded, 503 when Unhealthy, with `HealthResponseWriter`'s
JSON: `status`, `duration`, and per check (`checks`) its `status`, `description`, `duration`, `data`, `error` (the
exception's message) and `tags`. The paths can be changed (`app.MapHealthEndpoints(paths => paths.LivePath = ...)`),
and the returned builder adds conventions to all three. It needs no `AspNetCore.HealthChecks.UI.Client`.

Before (each service):

```csharp
app.MapHealthChecks("health", new HealthCheckOptions { ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse }).AllowAnonymous();
app.MapHealthChecks("health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready"), ResponseWriter = ... }).AllowAnonymous();
app.MapHealthChecks("health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = ... }).AllowAnonymous();
```

After: `app.MapHealthEndpoints();`. A service whose liveness ran no check (`Predicate = _ => false`) gets the same
answer as long as no check is tagged `live`. The JSON's shape is this package's (`checks`, `duration`, `error`), not
HealthChecks.UI's (`entries`, `totalDuration`, `exception`): a probe reads only the status code.

### Calling another service: resilience keyed on ErrorType

```csharp
services
    .AddHttpClient<ISwgohApiClient, SwgohApiClient>(client => client.BaseAddress = new Uri(url))
    .AddResultResilience(resilience => resilience.AlsoRetry.Add(StatusCodes.Status404NotFound));
```

Built on Microsoft.Extensions.Http.Resilience (Polly 8), which the service references itself. Outermost to
innermost: the total timeout (30 s), the retries, the circuit breaker, the attempt timeout (10 s).

- **What is retried** (3 times by default, `MaxRetries`; 1 s doubling to at most 30 s, with jitter): a response whose
  status `ToResultAsync` reads as a transient `ErrorType` (`Failure`, `ExternalFailure`, `Timeout`, `RateLimit`: a
  5xx, a 429, and any other status it doesn't map, such as 408), a network error (`HttpRequestException`), an attempt
  that timed out. Not a 400, 401, 403, 404 or 409: those are answers. Never the caller's cancellation.
- **`AlsoRetry`** adds statuses (e.g. 404 from an upstream that is still catching up); **`NeverRetry`** removes
  them, and wins over `AlsoRetry`.
- **`Retry-After`** (a delay or a date) on a retried response is waited instead of the computed delay; one at least
  as long as what is left of `TotalTimeout` isn't waited for, and the caller gets that response (e.g. a 429) at once.
- **The circuit breaker** opens when 10 % of at least 100 calls in 30 s fail transiently (`AlsoRetry` statuses don't
  count), for 5 s; `CircuitBreaker = false` turns it off.
- **Time** is the `TimeProvider` in the container: every delay, timeout and `Retry-After` date runs on a
  `FakeTimeProvider` in tests.

After the retries the caller gets the last response (`ToResultAsync` turns it into a failure); a timeout or an open
circuit throws `TimeoutRejectedException` / `BrokenCircuitException`, as with `AddStandardResilienceHandler`.

Before (Shard and Tournament):

```csharp
.AddPolicyHandler(HttpPolicyExtensions
    .HandleTransientHttpError()
    .OrResult(msg => msg.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.TooManyRequests)
    .WaitAndRetryAsync(6, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));
```

After: `.AddResultResilience(resilience => resilience.AlsoRetry.Add(404))`, and drop `Microsoft.Extensions.Http.Polly`.
Six retries up to 64 s apart don't fit in the default 30 s total: raise `TotalTimeout` and `MaxRetries` to keep that
patience, or take the defaults.

### Accepting Resrcify.Identity's tokens

```json
{ "Jwt": { "Authority": "http://identity:22000", "Issuer": "Resrcify.Identity", "Audience": "Resrcify" } }
```

```csharp
services.AddResrcifyJwtBearer(configuration);
services.AddAuthorizationBuilder(); // the service's own policies
```

The `Jwt` section (`ResrcifyJwtOptions`) is bound and validated at start-up (`Authority` an absolute http(s) URI,
`Issuer` and `Audience` set). JWT bearer becomes the default scheme; its signing keys come from the authority's
discovery document (JWKS, refreshed when Identity rotates its keys), and the issuer, audience, lifetime and signature
are checked. `RequireHttpsMetadata` is off unless set: the services reach Identity over plain HTTP in the cluster.
The optional `Action<JwtBearerOptions>` runs after, for events or claim mapping. The service references
`Microsoft.AspNetCore.Authentication.JwtBearer` itself. It replaces Sandbox's `JwtOptions` and `ConfigureJwtOptions`
(same section, same keys).

## Common issues

- If response serialization differs between minimal APIs and controllers, ensure both `JsonOptions` and MVC JSON options are configured.
- If status codes look wrong, verify error classification (`ErrorType`) at the source.

## Sample project

See `samples/Resrcify.SharedKernel.WebApiExample` for practical usage in controllers and endpoint flows.