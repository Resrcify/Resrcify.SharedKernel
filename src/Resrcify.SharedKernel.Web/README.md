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
    - [Endpoints for a mediator request](#endpoints-for-a-mediator-request)
    - [Idempotency keys](#idempotency-keys)
    - [Read an HTTP response as a result](#read-an-http-response-as-a-result)
    - [Unhandled exceptions: the same problem details](#unhandled-exceptions-the-same-problem-details)
    - [Health endpoints](#health-endpoints)
    - [The message bus' error queue](#the-message-bus-error-queue)
    - [The outbox](#the-outbox)
    - [Calling another service: resilience keyed on ErrorType](#calling-another-service-resilience-keyed-on-errortype)
    - [Accepting Resrcify.Identity's tokens](#accepting-resrcifyidentitys-tokens)
    - [API versioning and the OpenAPI documents](#api-versioning-and-the-openapi-documents)
    - [The CORS policy](#the-cors-policy)
  - [Common issues](#common-issues)
  - [Sample project](#sample-project)

## What you get

- `ApiController` base type in `Primitives/`.
- Result-to-HTTP conversion extensions in `Extensions/`.
- `MapGetRequest` / `MapPostRequest` / `MapPutRequest` / `MapPatchRequest` / `MapDeleteRequest`: a Minimal-API endpoint
  that sends the mediator request its delegate returns and answers with its result, with its OpenAPI responses.
- `IdempotencyHeaders`: the `Idempotency-Key` header for the mediator's idempotent requests, and the
  `Idempotency-Replayed` mark the request endpoints put on a replayed answer.
- Consistent mapping from `ErrorType` to HTTP problem responses.
- `AddResultProblemDetails()`: unhandled exceptions answered with the same problem details as a failed result, and a
  minimal-API parameter that fails to bind with a 400 naming it, in every environment (with
  `app.UseServiceDefaults()`, Observability, or `app.UseExceptionHandler()`).
- `MapHealthEndpoints()`: `/health`, `/health/ready` and `/health/live`, anonymous, with a JSON response writer.
- `MapErrorQueueAdministration()`: the message bus' error queue under `/admin/error-queue` (show, delete, move back).
- `MapOutboxAdministration<TContext>()`: a DbContext's outbox under `/admin/outbox` (what waits and what gave up,
  try again, delete).
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

### Endpoints for a mediator request

The flow above is the body of most endpoints. `MapGetRequest`, `MapPostRequest`, `MapPutRequest`, `MapPatchRequest`,
`MapDeleteRequest` and `MapRequest(pattern, methods, ...)` write it for you. The delegate is an ordinary Minimal-API
handler that returns the request: its parameters bind as usual (route values, query string, headers, body,
`[AsParameters]`, services, `HttpContext`, a `CancellationToken`), and it may be async. The endpoint sends the request
through `ISender` and answers:

| The request is answered with | Success | Failure |
|---|---|---|
| `Result<T>` (`IQuery<T>`, `ICachingQuery<T>`, `ICommand<T>`, `ITransactionCommand<T>`, …) | 200 with the value | problem details |
| `Result` (`ICommand`, `ITransactionCommand`, …) | 204 | problem details |

Any request answered with a `Result` works, a type of your own implementing `IRequest<Result>` or
`IRequest<Result<T>>` too, and it goes through the mediator's whole pipeline (validation, caching, transactions). A
delegate returning anything else fails when the endpoint is mapped, not when it is called.

```csharp
app.MapPostRequest(ApiEndpoints.Shards.AddShardMember,
        (Guid shardId, long allyCode, AddShardMemberBody body) => new AddShardMemberCommand(shardId, allyCode, body.Emoji))
    .WithName("Shards.AddShardMember")
    .WithTags(ApiEndpoints.Shards.Tag)
    .WithApiVersionSet(ApiVersioning.VersionSet)
    .HasApiVersion(new ApiVersion(1, 0));

app.MapGetRequest(ApiEndpoints.Shards.GetShard, (Guid shardId) => new GetShardByIdQuery(shardId));
app.MapPostRequest(ApiEndpoints.Shards.Search, (SearchBody body) => new SearchShardsQuery(body.Name));   // a query by POST
app.MapGetRequest(ApiEndpoints.Shards.GetAll, () => new GetAllActiveShardsQuery());
```

**Other answers.** `onSuccess` replaces 200/204: a delegate taking the value (or nothing) and returning an `IResult`. A
`TypedResults` type describes itself for OpenAPI:

```csharp
app.MapPostRequest(ApiEndpoints.Shards.Create, (CreateShardBody body) => new CreateShardCommand(body.Name),
    onSuccess: (ShardDto shard) => TypedResults.Created($"/shards/{shard.Id}", shard));          // 201
app.MapPostRequest(ApiEndpoints.Jobs.Run, (string job) => new RunJobCommand(job),
    onSuccess: () => TypedResults.Accepted("/jobs"));                                           // 202
app.MapGetRequest(ApiEndpoints.Shards.GetShard, (Guid shardId) => new GetShardByIdQuery(shardId),
    onSuccess: (GetShardByIdResponse response) => TypedResults.Ok(response.Shard));              // part of the result
```

`onFailure` replaces problem details for one endpoint; for the whole app, configure `RequestEndpointOptions`:
`services.Configure<RequestEndpointOptions>(options => options.OnFailure = ...)`. Its `ProblemStatusCodes` (400, 404,
409 and 500) are the problems every endpoint declares for OpenAPI; add an endpoint's own with `ProducesProblem`.

The endpoints return the `RouteHandlerBuilder`, so naming, tags, authorization and the API version chain as usual, and
they work in route groups. A filter added to an endpoint runs inside it and may answer instead (a validation filter's
problem, say). The request itself is never described as a response.

### Idempotency keys

Idempotency is the mediator's (`IIdempotentRequest`, see the Mediator package): it works however a request arrives.
Over HTTP, the endpoint passes the client's `Idempotency-Key` header to the request, and a repeat is answered with the
first result: the same 200/201/204, marked `Idempotency-Replayed: true` by the request endpoints.

```csharp
public sealed record CreateShardCommand(string Name) : ICommand<ShardDto>, IIdempotentRequest
{
    public string? IdempotencyKey { get; init; }
    public string? IdempotencyScope { get; init; }   // whose keys: the user's ID
}

app.MapPostRequest(ApiEndpoints.Shards.Create,
    (CreateShardBody body, [FromHeader(Name = IdempotencyHeaders.Key)] string? key, ClaimsPrincipal user) =>
        new CreateShardCommand(body.Name) { IdempotencyKey = key, IdempotencyScope = user.GetUserId().Value.ToString() },
    onSuccess: (ShardDto shard) => TypedResults.Created($"/shards/{shard.Id}", shard));
```

A repeat while the first is still handled is answered 409, a key used for a different request 422, and a blank or too
long key 400. An endpoint of your own marks a replay with `IdempotencyHeaders.MarkIfReplayed(httpContext, request)`
after sending. A key the endpoint must have is a validation rule on the request (`RuleFor(c => c.IdempotencyKey).NotEmpty()`).

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
app.UseServiceDefaults();   // Observability: the request logging, then UseExceptionHandler()
```

`app.UseServiceDefaults()` (Resrcify.SharedKernel.Observability) starts the pipeline with the request logging and
then `UseExceptionHandler()`, the order they need: the request's line logs the status the handler answered and the
stack is logged once, by the handler. A service without the Observability package, or one that must put something of
its own between the two, calls `app.UseExceptionHandler()` itself, early in the pipeline, after
`app.UseServiceRequestLogging()` when it has it:

```csharp
app.UseServiceRequestLogging();
app.UseExceptionHandler();
```

An exception no handler caught is answered like a failed result (`ToProblemDetails`): a 500 problem details whose
`errors` hold one `Failure` error coded `Unhandled` (`ErrorCodes.Request.Unhandled`), so a caller's
`ToResultAsync` reads it back as that error. Outside Development the message is only "An unexpected error occurred.";
in Development the error's message is the exception's type and message and the problem's `detail` the whole exception
(`options.IncludeExceptionDetails` decides either way). The exception is logged once, at `Error` (the exception
handler middleware logs nothing more for an exception a handler handled), following Results' `LoggedExceptions`
convention: when the mediator's behaviors logged it already on its way out, the handler logs a `Debug` line without the
stack instead, and answers the same 500. A request the client aborted (an `OperationCanceledException` or `IOException`
once `RequestAborted` fired) never reaches the handler: `UseExceptionHandler()` answers it with a 499 itself, logged at
`Debug`, not as an error. A
`TransientFailureException` (Results; itself or among the causes) holding errors is answered as the failed result it
stands for, with problem details of its `Errors` (a 502 for an upstream's failure, a 504 for a timeout ...), logged at
`Warning`, once.

Any other exception the container's `ITransientFailureClassifier` (Abstractions) calls transient is answered 503, with a
`Retry-After` (`options.TransientFailureRetryAfter`, 1 second by default, in whole seconds rounded up) and problem
details whose `errors` hold one `Failure` error coded `Unavailable` (`ErrorCodes.Request.Unavailable`), logged at
`Warning`, once (a `Debug` line when a mediator behavior logged it already). It is the classifier the outbox, the jobs
and the mediator's behaviors judge by: a transient database failure (`DbException.IsTransient`, e.g. a dropped
connection), a timeout or a handler's own cancellation, an HTTP call without an answer or with a 408, 429 or 5xx, a
`TransientFailureException` without errors, and whatever a registered `ITransientFailureRule` adds.
`AddResultProblemDetails()` registers the default classifier unless the service registered one. A caller's
`ToResultAsync` reads the answer back as a transient failure, and the resilience preset of a client retries a 503 after
its `Retry-After`. Any other exception stays the 500 above.

A request ASP.NET Core couldn't bind (its `BadHttpRequestException`: a required query, route, header or body value left
out, a value that doesn't parse, such as `/shards/abc` for a `Guid id`, a body that isn't valid JSON) isn't a fault of
the service: it is answered with the exception's status (400, or 413, 415 ...) and problem details whose `errors` hold
one `Validation` error coded with the parameter's name (`ErrorCodes.Request.Binding`, `Request`, when
the exception names none), logged at `Debug`:

```json
{ "status": 400, "title": "Bad Request", "errors": [ { "code": "page",
  "message": "The required parameter \"page\" was not provided from query string." } ] }
```

The message names the parameter only, never its type or the value sent; in Development (`IncludeExceptionDetails`)
the problem's `detail` holds ASP.NET Core's own message. Minimal APIs throw the exception only when
`RouteHandlerOptions.ThrowOnBadRequest` is on, which ASP.NET Core does in Development only (elsewhere an empty 400),
so `AddResultProblemDetails()` turns it on in every environment. To keep ASP.NET Core's default, set
`options.ProblemDetailsForBindingFailures = false` (a `BadHttpRequestException` that reaches the handler is still
answered as above). A service's own handler for it (Oracle's `BadRequestExceptionHandler`) and its
`ThrowOnBadRequest = true` can go.

It replaces `builder.Services.AddProblemDetails()` (it registers it); keep `app.UseExceptionHandler()`, or
`app.UseServiceDefaults()`, which adds it.

### Health endpoints

```csharp
builder.Services.AddHealthChecks()
    .AddPostgres<ShardDbContext>()          // UnitOfWork.Postgres: ready by default
    .AddMessageBus()                        // MessageBus: ready by default
    .AddOutbox<ShardDbContext>()            // UnitOfWork: ready by default (Degraded on a backlog)
    .AddQuartzJobs()                        // UnitOfWork: live by default
    .AddCheck<UpstreamHealthCheck>("upstream", tags: [HealthTags.Ready]);
// ...
app.MapHealthEndpoints();
```

`HealthTags` (`Ready`, `Live`) lives in Resrcify.SharedKernel.Abstractions (`Resrcify.SharedKernel.Abstractions.Health`),
so every package's checks share it: given no tags, SharedKernel's checks take `HealthTags.Ready` (`AddPostgres`,
`AddMessageBus`, `AddOutbox`) or `HealthTags.Live` (`AddQuartzJobs`); pass `tags: []` for none.

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

### The message bus' error queue

```csharp
app.MapErrorQueueAdministration()                       // under /admin/error-queue
    .RequireAuthorization(policy => policy.RequireRole("Admin"));
```

| Endpoint | |
|---|---|
| `GET /?take=100` | the first messages, oldest first (1 to 1000), with why each failed, and how many there are |
| `DELETE /messages/{id}` | deletes a message: 204, or 404 |
| `DELETE /?type=` | deletes every message, or every one of the type: `{ "deleted": n }` |
| `POST /messages/{id}/retry` | moves a message back to the queue it failed on: 204, 404, or 409 when it can't be moved |
| `POST /retry?type=` | moves every message, or every one of the type, back: `{ "retried": n }` |

They answer through `IErrorQueue` (registered by the MessageBus package's `AddMessageBus`; see its README), and require
an authenticated caller; add the admin policy on the group returned. Messages hold event content: never open these to
everyone.

### The outbox

So an operator never needs hand-written SQL to see what is stuck in a DbContext's outbox or to try it again:

```csharp
app.MapOutboxAdministration<AppDbContext>()             // under /admin/outbox
    .RequireAuthorization(policy => policy.RequireRole("Admin"));
```

| Endpoint | |
|---|---|
| `GET /` | messages waiting, retrying and given up, in all and per event type (with its lane) |
| `GET /given-up?type=&take=` | the messages that gave up, most recent first (50 by default, 1 to 500) |
| `GET /messages/{id}` | a message with its content, where it stands and its last error; 404 |
| `POST /messages/{id}/retry` | tries a message that gave up again: 204, or 404 when it didn't give up |
| `POST /given-up/retry?type=` | tries every message that gave up, or every one of the type, again: `{ "retried": n }` |
| `DELETE /messages/{id}` | deletes a message that gave up, for good: 204, or 404 when it didn't give up |
| `DELETE /given-up?type=` | deletes every message that gave up, or every one of the type: `{ "deleted": n }` |

They answer through `IOutboxAdministration<AppDbContext>` (Abstractions), which the UnitOfWork package registers with
the outbox (`AddOutboxProcessing`, `AddProcessOutboxMessagesJob`, `AddOutboxLanes`): this package doesn't reference
UnitOfWork. A retried message starts from its first try, keeps its last error until the next one, and the outbox is
woken so it is handled at once; a deleted one's event is never handled. Only messages that gave up are tried again or
deleted. The endpoints are named `Outbox.<context>.<operation>` (`Outbox.AppDbContext.Summary`, ...), require an
authenticated caller (add the admin policy on the group returned), and messages hold event content: never open these to
everyone.

### Calling another service: resilience keyed on ErrorType

```csharp
services
    .AddHttpClient<ISwgohApiClient, SwgohApiClient>(client => client.BaseAddress = new Uri(url))
    .AddResultResilience(resilience => resilience.AlsoRetry.Add(StatusCodes.Status404NotFound));
```

Built on Microsoft.Extensions.Http.Resilience (Polly 8), which comes with the package. Outermost to
innermost: the answer for a timeout or an open circuit, the total timeout (30 s), the retries, the circuit breaker,
the attempt timeout (10 s).

- **What is retried** (`Retries`, a `RetrySchedule`: 4 attempts by default, the first included, 1 s doubling to at
  most 30 s, with jitter; `MaxAttempts = 1` turns retrying off): a response whose
  status `ToResultAsync` reads as a transient `ErrorType` (`Failure`, `ExternalFailure`, `Timeout`, `RateLimit`: a
  5xx, a 408 or a 429), a network error (`HttpRequestException`), an attempt that timed out. Not a 400, 401, 403, 404,
  409 or 422: those are answers. Never the caller's cancellation.
- **`AlsoRetry`** adds statuses (e.g. 404 from an upstream that is still catching up); **`NeverRetry`** removes
  them, and wins over `AlsoRetry`.
- **`RetryOnly`** replaces the rule with a list: only those statuses are retried (network errors and timed-out
  attempts still are), any other is returned at once and doesn't count against the circuit. It can't be combined
  with `AlsoRetry` or `NeverRetry` and holds 400–599 only: the host doesn't start otherwise (see
  [Options, validated when the host starts](#options-validated-when-the-host-starts)).
  `ResultResilienceOptions.SafeToRetryStatuses` (408, 429, 503) is
  the list for a service that retries its own failures, so that its other 5xx are final:

  ```csharp
  // Was: foreach (var status in Enumerable.Range(500, 100).Where(s => s != 503)) resilience.NeverRetry.Add(status);
  client.AddResultResilience(resilience => resilience.RetryOnly = ResultResilienceOptions.SafeToRetryStatuses);
  // More: resilience.RetryOnly = new HashSet<int>(ResultResilienceOptions.SafeToRetryStatuses) { 404 };
  ```

- **`Retry-After`** (a delay or a date) on a retried response is waited instead of the computed delay; one at least
  as long as what is left of `TotalTimeout` isn't waited for, and the caller gets that response (e.g. a 429) at once.
- **The circuit breaker** opens when 10 % of at least 100 calls in 30 s fail transiently (`AlsoRetry` statuses don't
  count), for 5 s; `CircuitBreaker = false` turns it off.
- **Time** is the `TimeProvider` in the container: every delay, timeout and `Retry-After` date runs on a
  `FakeTimeProvider` in tests.
- **HttpClient's own timeout** (`HttpClient.Timeout`, 100 s by default) never ends a call before the pipeline does:
  when it is shorter than `TotalTimeout` plus `ResultResilienceHttpClientBuilderExtensions.HttpClientTimeoutMargin`
  (5 s), it is raised to that, after every other configuration of the client (a `ConfigureHttpClient` before or after
  `AddResultResilience` alike). A longer one the service set, or `Timeout.InfiniteTimeSpan`, is kept. So a hung call
  ends as `ResultResilienceErrors.Timeout`, never as HttpClient's `TaskCanceledException`, whatever `TotalTimeout` is:
  a service no longer keeps its timeouts "inside HttpClient's 100 s", nor sets `HttpClient.Timeout` for them.

#### Options, validated when the host starts

The options are the client's named `ResultResilienceOptions` (named after the client), so a service configures them
in code, from a configuration section, or both, and never reads `IConfiguration` itself:

```csharp
// appsettings.json: "SwgohApiService": { "Resilience": { "AttemptTimeout": "00:01:00", "TotalTimeout": "00:01:35",
//   "CircuitBreaker": false, "Retries": { "MaxAttempts": 4, "FirstDelay": "00:00:01", "MaxDelay": "00:00:04", "Jitter": true } } }
services.AddHttpClient<ISwgohApiClient, SwgohApiClient>()
    .AddResultResilience("SwgohApiService:Resilience", resilience =>
        resilience.RetryOnly = ResultResilienceOptions.SafeToRetryStatuses);
```

The `configure` action runs first (the service's own defaults, in code), then the section is bound
(`BindConfiguration`) when the options are first read: a value the section holds wins (a test's shorter timeouts), one
it leaves out keeps the code's or the default, `Retries`' values one by one too (`Retries:FirstDelay` alone keeps the
attempts). Either way the options are validated when the host starts (`ValidateOnStart`): a
host whose options are unusable doesn't start, with an `OptionsValidationException` naming the client and the problem:
an `AttemptTimeout` longer than `TotalTimeout` (or one that isn't positive), a negative circuit-breaker duration, a
failure ratio outside 0–1, no `Retries`, or a `RetryOnly` combined with `AlsoRetry` / `NeverRetry` or holding a status
outside 400–599. A container used without a host gets that exception when it first builds the pipeline.

Before (Sentinel's `SwgohApiResilienceOptions`): a class of its own with the same properties and production's values,
a `Validate` method keeping `TotalTimeout` under HttpClient's 100 s, `AddOptions<...>().BindConfiguration(...).ValidateOnStart()`,
and `configuration.GetSection(...).Get<...>()` to copy each value into `AddResultResilience`. After: production's
values in the `configure` action, the tests' overrides under the same section (`Retries:FirstDelay` /
`Retries:MaxDelay` for `RetryDelay` / `MaxRetryDelay`), and the one call:

```csharp
client.AddResultResilience("SwgohApiService:Resilience", resilience =>
{
    resilience.Retries = new RetrySchedule(4, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), Jitter: true);
    resilience.AttemptTimeout = TimeSpan.FromSeconds(60);
    resilience.TotalTimeout = TimeSpan.FromSeconds(95);
    resilience.CircuitBreaker = false;
    resilience.RetryOnly = ResultResilienceOptions.SafeToRetryStatuses;
});
```

After the retries the caller gets the last response (`ToResultAsync` turns it into a failure). A timeout (the last
attempt's, or the whole call's), an open circuit or a service that couldn't be reached after the retries is answered in
the service's place, so `ToResultAsync` reads it as a failure like any other, with no try/catch:

| Gave up on | Response | `ToResultAsync`'s error |
|---|---|---|
| a timeout (`TimeoutRejectedException`) | 504, problem details | `ResultResilienceErrors.Timeout` (`Http.ResilienceTimeout`, `Timeout`) |
| an open circuit (`BrokenCircuitException`) | 502, problem details | `ResultResilienceErrors.CircuitOpen` (`Http.CircuitOpen`, `ExternalFailure`) |
| no answer (`HttpRequestException` without a status: refused, reset, a name that doesn't resolve) | 502, problem details | `ResultResilienceErrors.Unreachable(authority, cause)` (`Http.Unreachable`, `ExternalFailure`), e.g. `The service at swgohapi:8080 could not be reached: Connection refused (swgohapi:8080)` |

The made-up response carries the header `Resrcify-Resilience: timeout` / `circuit-open` / `unreachable` (a response
from the service called never does) and the request; `EnsureSuccessStatusCode` throws `HttpRequestException` for it, as
for a real 504. The caller's own cancellation still throws `OperationCanceledException`.
`ThrowTimeoutsAndOpenCircuits = true` throws `TimeoutRejectedException` / `BrokenCircuitException` /
`HttpRequestException` instead, as `AddStandardResilienceHandler` (and 4.0.0) does. A failure while HttpClient reads the
body of an answer it got (a connection reset halfway through a buffered response) still throws an
`HttpRequestException`: it happens after the pipeline returned the response, outside it.

`AddResultResilience` also adds the Web package's `ITransientFailureRule` (Abstractions): Polly's rejections
(`ExecutionRejectedException`: a timeout, a broken or isolated circuit, the rate limiter), when they are thrown, are
transient to the outbox, the mediator's logging and the message bus. The other packages never name Polly's types.

Before (Shard and Tournament):

```csharp
.AddPolicyHandler(HttpPolicyExtensions
    .HandleTransientHttpError()
    .OrResult(msg => msg.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.TooManyRequests)
    .WaitAndRetryAsync(6, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));
```

After: `.AddResultResilience(resilience => resilience.AlsoRetry.Add(404))`, and drop `Microsoft.Extensions.Http.Polly`.
Six retries up to 64 s apart don't fit in the default 30 s total: raise `TotalTimeout` and `Retries.MaxAttempts` to keep that
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
The optional `Action<JwtBearerOptions>` runs after, for events or claim mapping.
`Microsoft.AspNetCore.Authentication.JwtBearer` comes with the package. It replaces Sandbox's `JwtOptions` and `ConfigureJwtOptions`
(same section, same keys).

### API versioning and the OpenAPI documents

The versioning every service sets up, in one call, and the version set its endpoints use
(`Resrcify.SharedKernel.Web.Versioning`):

```csharp
// Infrastructure (or wherever the service registers its HTTP surface)
services.AddServiceApiVersioning();                                            // the version in the query string or URL
services.AddServiceApiVersioning(new MediaTypeApiVersionReader("api-version")); // the version in the media type

// Program.cs
app.MapApiEndpoints();
if (app.Environment.IsDevelopment())
    app.MapOpenApi().WithDocumentPerVersion();

// An endpoint
app.MapGet(Routes.Shards.Get, handler)
    .WithApiVersionSet(ApiVersioning.VersionSet)
    .HasApiVersion(new ApiVersion(1, 0));
```

`AddServiceApiVersioning` reports the supported versions (`api-supported-versions`), takes a request without a version
as 1.0, groups the API explorer by `'v'VVV` with the version substituted in the URL, and adds an OpenAPI document per
version (`/openapi/v1.json`). Its `versionReader` is where a request names its version: left out, Asp.Versioning's
own (the `api-version` query string, or a URL segment); `MediaTypeApiVersionReader("api-version")` reads
`Accept: application/json; api-version=1.0` instead (an unsupported version there is a 406, in the query string a
400). It is wire behaviour: keep the reader the service has. `configureOpenApi` configures the documents
(`options => options.Document.AddSchemaTransformer(...)`). It returns Asp.Versioning's builder, for anything more.
`ApiVersioning.VersionSet` is version 1.0, reported. It is complete from the start and no endpoint changes it, so the
hosts of a process (tests' hosts, side by side) share it safely: Asp.Versioning's own set records every version an
endpoint declares in a collection that isn't safe to write from two threads, and before 4.11.0 hosts building their
endpoints at the same time could corrupt it. An endpoint whose version the set doesn't hold fails as the endpoints are
built, saying so; give it a version set of its own (`app.NewApiVersionSet()`). `app.CreateApiVersionSet()` has nothing
left to build: a `Program` that calls it needn't change, and a new one leaves it out.

### The CORS policy

```csharp
services.AddServiceCors("ShardCors");   // Presentation's registration
app.UseCors("ShardCors");               // Program.cs
```

A named policy allowing any origin, header and method, without credentials: the one every service defined. CORS isn't
the authorization boundary: bearer tokens, which a browser never attaches on its own, guard every non-anonymous route,
and `AllowAnyOrigin` excludes `AllowCredentials`, so no cookie or credentialed cross-site request rides it. A service
that restricts its origins registers a policy of its own.

## Common issues

- If response serialization differs between minimal APIs and controllers, ensure both `JsonOptions` and MVC JSON options are configured.
- If status codes look wrong, verify error classification (`ErrorType`) at the source.

## Sample project

See `samples/Resrcify.SharedKernel.WebApiExample` for practical usage in controllers and endpoint flows.