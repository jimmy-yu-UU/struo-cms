# Phase 9a — Unified Response Envelope Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every REST `/api/*` JSON response one consistent envelope — success `{ success:true, data, meta? }` and error `{ success:false, error:{ code, message, details? } }` — applied automatically and centrally, without touching GraphQL, the frontend, or the domain layers.

**Architecture:** Enveloping moves out of hand-written controller returns into three centralized `Struo.Api` seams: an `IAlwaysRunResultFilter` wraps successful/bare-404 MVC results, an `IExceptionHandler` maps domain exceptions to error envelopes (replacing the inline `try/catch` middleware in `Program.cs`), and an `InvalidModelStateResponseFactory` envelopes model-binding validation. Controllers are simplified to return raw data / a `PagedResult` marker / a `Fail(...)` helper. Error envelopes are built at source (never double-wrapped) and success envelopes are built by the filter; a shared `Envelope` factory keeps the shape DRY.

**Tech Stack:** .NET 10 / C#, ASP.NET Core Controllers, System.Text.Json (camelCase), xUnit + AwesomeAssertions, `WebApplicationFactory<Program>` integration host (`ApiFactory`), SQLite for tests / PostgreSQL for the live gate.

## Global Constraints

- **`Struo.Api` only** — `Struo.Domain` / `Struo.Application` / `Struo.Infrastructure` are **untouched**.
- **No new NuGet packages.** `IExceptionHandler` / `AddExceptionHandler` / `AddProblemDetails` are ASP.NET Core shared-framework built-ins, not packages.
- **No DB migration.** No schema/persistence change of any kind.
- **GraphQL is untouched** — `/graphql` keeps its `{ data, errors }` spec envelope and its `StruoErrorFilter` code mapping.
- **Frontend is untouched this slice** — explicit `apiClient`/`ApiError` alignment is deferred to **9a-fe**. The current frontend is backward-compatible (`apiClient` unwraps `payload?.data ?? payload`, reads `payload?.error?.message`; `itemsApi.list` reads `res.data` + `res.meta.total`).
- **Outbound JSON is camelCase** (existing MVC + `WriteAsJsonAsync` web defaults).
- **`dotnet build -warnaserror` must stay at 0 warnings.**
- **Error code taxonomy (verbatim):** `UNAUTHORIZED`(401) / `FORBIDDEN`(403) / `NOT_FOUND`(404) / `CONFLICT`(409) / `BAD_USER_INPUT`(400) / `VALIDATION`(400,+details) / `INTERNAL_SERVER_ERROR`(500, masked).
- **Not enveloped:** `204 No Content` (bare), `FileResult` binary streams, `RedirectResult` (302), `ChallengeResult`/`EmptyResult`/`SignIn`/`SignOut`.
- Baseline before this slice: backend `dotnet test` **599**, frontend `pnpm test` **261**.

---

### Task 1: Envelope types + factory + error-code map

Pure types and a status→code helper. No wiring; the running app is unchanged after this task.

**Files:**
- Create: `src/Struo.Api/Http/Envelope.cs`
- Create: `src/Struo.Api/Http/ErrorCodes.cs`
- Test: `tests/Struo.Tests/Api/EnvelopeTypesTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `record ValidationDetail(string Field, string Message)`
  - `record ErrorBody(string Code, string Message, IReadOnlyList<ValidationDetail>? Details = null)`
  - `record MetaInfo(long Total, int Limit, int Offset)`
  - `record SuccessEnvelope(bool Success, object? Data, MetaInfo? Meta = null)`
  - `record ErrorEnvelope(bool Success, ErrorBody Error)`
  - `record PagedResult(object Data, long Total, int Limit, int Offset)`
  - `static class Envelope { SuccessEnvelope Success(object? data, MetaInfo? meta = null); ErrorEnvelope Error(string code, string message, IReadOnlyList<ValidationDetail>? details = null); }`
  - `static class ErrorCodes { const string Unauthorized/Forbidden/NotFound/Conflict/BadUserInput/Validation/Internal; string ForStatus(int status); }`

- [ ] **Step 1: Write the failing test**

`tests/Struo.Tests/Api/EnvelopeTypesTests.cs`:
```csharp
using System.Text.Json;
using AwesomeAssertions;
using Struo.Api.Http;
using Xunit;

namespace Struo.Tests.Api;

public class EnvelopeTypesTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(400, "BAD_USER_INPUT")]
    [InlineData(401, "UNAUTHORIZED")]
    [InlineData(403, "FORBIDDEN")]
    [InlineData(404, "NOT_FOUND")]
    [InlineData(409, "CONFLICT")]
    [InlineData(418, "INTERNAL_SERVER_ERROR")]
    [InlineData(500, "INTERNAL_SERVER_ERROR")]
    public void ForStatus_maps_status_to_code(int status, string expected) =>
        ErrorCodes.ForStatus(status).Should().Be(expected);

    [Fact]
    public void Success_omits_meta_when_null()
    {
        var json = JsonSerializer.Serialize(Envelope.Success(new { id = 1 }), Web);
        json.Should().Contain("\"success\":true");
        json.Should().Contain("\"data\":");
        json.Should().NotContain("meta");
    }

    [Fact]
    public void Success_includes_meta_when_present()
    {
        var json = JsonSerializer.Serialize(Envelope.Success(new[] { 1 }, new MetaInfo(42, 25, 0)), Web);
        json.Should().Contain("\"meta\":{\"total\":42,\"limit\":25,\"offset\":0}");
    }

    [Fact]
    public void Error_carries_code_and_message_and_omits_details_when_null()
    {
        var json = JsonSerializer.Serialize(Envelope.Error("BAD_USER_INPUT", "nope"), Web);
        json.Should().Contain("\"success\":false");
        json.Should().Contain("\"code\":\"BAD_USER_INPUT\"");
        json.Should().Contain("\"message\":\"nope\"");
        json.Should().NotContain("details");
    }

    [Fact]
    public void Error_includes_details_when_present()
    {
        var json = JsonSerializer.Serialize(
            Envelope.Error("VALIDATION", "bad", new[] { new ValidationDetail("email", "required") }), Web);
        json.Should().Contain("\"details\":[{\"field\":\"email\",\"message\":\"required\"}]");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~EnvelopeTypesTests`
Expected: FAIL — `Struo.Api.Http` / `Envelope` / `ErrorCodes` do not exist (compile error).

- [ ] **Step 3: Write the implementation**

`src/Struo.Api/Http/Envelope.cs`:
```csharp
using System.Text.Json.Serialization;

namespace Struo.Api.Http;

public sealed record ValidationDetail(string Field, string Message);

public sealed record ErrorBody(
    string Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ValidationDetail>? Details = null);

public sealed record MetaInfo(long Total, int Limit, int Offset);

/// <summary>Success wire envelope; <c>Meta</c> is omitted from JSON when null.</summary>
public sealed record SuccessEnvelope(
    bool Success,
    object? Data,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    MetaInfo? Meta = null);

public sealed record ErrorEnvelope(bool Success, ErrorBody Error);

/// <summary>Marker a controller returns for a paginated list; the result filter unwraps it to data + meta.</summary>
public sealed record PagedResult(object Data, long Total, int Limit, int Offset);

public static class Envelope
{
    public static SuccessEnvelope Success(object? data, MetaInfo? meta = null) => new(true, data, meta);

    public static ErrorEnvelope Error(string code, string message, IReadOnlyList<ValidationDetail>? details = null) =>
        new(false, new ErrorBody(code, message, details));
}
```

`src/Struo.Api/Http/ErrorCodes.cs`:
```csharp
namespace Struo.Api.Http;

public static class ErrorCodes
{
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string BadUserInput = "BAD_USER_INPUT";
    public const string Validation = "VALIDATION";
    public const string Internal = "INTERNAL_SERVER_ERROR";

    public static string ForStatus(int status) => status switch
    {
        400 => BadUserInput,
        401 => Unauthorized,
        403 => Forbidden,
        404 => NotFound,
        409 => Conflict,
        _ => Internal,
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~EnvelopeTypesTests`
Expected: PASS (all cases).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/Http/Envelope.cs src/Struo.Api/Http/ErrorCodes.cs tests/Struo.Tests/Api/EnvelopeTypesTests.cs
git commit -m "feat(9a): envelope types, factory, and error-code map"
```

---

### Task 2: `EnvelopeResultFilter.BuildEnvelope` (pure result-rewriting logic)

The filter class plus a **pure static** `BuildEnvelope` that decides the replacement result. Registered nowhere yet — the running app is unchanged.

**Files:**
- Create: `src/Struo.Api/Http/EnvelopeResultFilter.cs`
- Test: `tests/Struo.Tests/Api/EnvelopeResultFilterTests.cs`

**Interfaces:**
- Consumes: `SuccessEnvelope`, `ErrorEnvelope`, `PagedResult`, `MetaInfo`, `Envelope`, `ErrorCodes` (Task 1).
- Produces:
  - `sealed class EnvelopeResultFilter : IAlwaysRunResultFilter`
  - `static IActionResult? EnvelopeResultFilter.BuildEnvelope(IActionResult result)` — returns the replacement result, or `null` to leave the result untouched.

- [ ] **Step 1: Write the failing test**

`tests/Struo.Tests/Api/EnvelopeResultFilterTests.cs`:
```csharp
using System.IO;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using Struo.Api.Http;
using Xunit;

namespace Struo.Tests.Api;

public class EnvelopeResultFilterTests
{
    [Fact]
    public void Ok_object_becomes_success_envelope()
    {
        var res = EnvelopeResultFilter.BuildEnvelope(new OkObjectResult("hi"));
        var obj = res.Should().BeOfType<ObjectResult>().Subject;
        var env = obj.Value.Should().BeOfType<SuccessEnvelope>().Subject;
        env.Success.Should().BeTrue();
        env.Data.Should().Be("hi");
        env.Meta.Should().BeNull();
    }

    [Fact]
    public void Paged_result_becomes_success_with_meta()
    {
        var res = EnvelopeResultFilter.BuildEnvelope(new OkObjectResult(new PagedResult(new[] { 1 }, 42, 25, 5)));
        var env = res.Should().BeOfType<ObjectResult>().Subject.Value.Should().BeOfType<SuccessEnvelope>().Subject;
        env.Meta.Should().Be(new MetaInfo(42, 25, 5));
        env.Data.Should().BeEquivalentTo(new[] { 1 });
    }

    [Fact]
    public void Existing_error_envelope_is_left_untouched()
    {
        var original = new ObjectResult(Envelope.Error("VALIDATION", "x")) { StatusCode = 400 };
        EnvelopeResultFilter.BuildEnvelope(original).Should().BeNull();
    }

    [Fact]
    public void Existing_success_envelope_is_left_untouched()
    {
        EnvelopeResultFilter.BuildEnvelope(new OkObjectResult(Envelope.Success("x"))).Should().BeNull();
    }

    [Fact]
    public void Bare_not_found_becomes_not_found_error_envelope()
    {
        var res = EnvelopeResultFilter.BuildEnvelope(new NotFoundResult());
        var obj = res.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(404);
        obj.Value.Should().BeOfType<ErrorEnvelope>().Which.Error.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public void No_content_is_left_untouched() =>
        EnvelopeResultFilter.BuildEnvelope(new NoContentResult()).Should().BeNull();

    [Fact]
    public void File_result_is_left_untouched() =>
        EnvelopeResultFilter.BuildEnvelope(new FileStreamResult(Stream.Null, "image/png")).Should().BeNull();

    [Fact]
    public void Redirect_result_is_left_untouched() =>
        EnvelopeResultFilter.BuildEnvelope(new RedirectResult("https://example/x")).Should().BeNull();

    [Fact]
    public void Non_2xx_object_becomes_error_envelope_by_status()
    {
        var res = EnvelopeResultFilter.BuildEnvelope(new ObjectResult("boom") { StatusCode = 409 });
        var obj = res.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(409);
        obj.Value.Should().BeOfType<ErrorEnvelope>().Which.Error.Code.Should().Be("CONFLICT");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~EnvelopeResultFilterTests`
Expected: FAIL — `EnvelopeResultFilter` does not exist (compile error).

- [ ] **Step 3: Write the implementation**

`src/Struo.Api/Http/EnvelopeResultFilter.cs`:
```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Struo.Api.Http;

/// <summary>
/// Wraps successful MVC results in <see cref="SuccessEnvelope"/> and converts bare error status
/// results into <see cref="ErrorEnvelope"/>. Results already carrying an envelope (produced by the
/// Fail helper / validation factory) are left untouched, so wrapping is idempotent. 204, binary file
/// streams, redirects, challenges and empty results pass through unchanged. The exception handler
/// writes its response directly and never reaches this filter.
/// </summary>
public sealed class EnvelopeResultFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (BuildEnvelope(context.Result) is { } replacement) context.Result = replacement;
    }

    public void OnResultExecuted(ResultExecutedContext context) { }

    /// <summary>Returns the replacement result, or <c>null</c> to leave the result untouched.</summary>
    public static IActionResult? BuildEnvelope(IActionResult result)
    {
        switch (result)
        {
            // Already an envelope (Fail helper / validation factory) — idempotent no-op.
            case ObjectResult { Value: ErrorEnvelope }:
            case ObjectResult { Value: SuccessEnvelope }:
                return null;

            // Paginated list marker → success envelope carrying meta.
            case ObjectResult { Value: PagedResult pr } paged:
                return new ObjectResult(Envelope.Success(pr.Data, new MetaInfo(pr.Total, pr.Limit, pr.Offset)))
                { StatusCode = paged.StatusCode };

            // Any other value-bearing result: 2xx → success, non-2xx → error by status.
            case ObjectResult obj:
            {
                var status = obj.StatusCode ?? StatusCodes.Status200OK;
                return status is >= 200 and < 300
                    ? new ObjectResult(Envelope.Success(obj.Value)) { StatusCode = obj.StatusCode }
                    : new ObjectResult(Envelope.Error(ErrorCodes.ForStatus(status), Message(obj.Value, status)))
                    { StatusCode = status };
            }

            // 204 success — stays bare.
            case NoContentResult:
                return null;

            // Bare error status result (no body), e.g. NotFound() / StatusCode(4xx).
            case StatusCodeResult { StatusCode: >= 400 and < 600 } scr:
                return new ObjectResult(Envelope.Error(ErrorCodes.ForStatus(scr.StatusCode), DefaultMessage(scr.StatusCode)))
                { StatusCode = scr.StatusCode };

            // FileResult / RedirectResult / ChallengeResult / EmptyResult / SignIn / SignOut / bare 2xx → untouched.
            default:
                return null;
        }
    }

    private static string Message(object? value, int status) =>
        value?.ToString() is { Length: > 0 } s ? s : DefaultMessage(status);

    private static string DefaultMessage(int status) => status switch
    {
        400 => "Bad request.",
        401 => "Authentication required.",
        403 => "Forbidden.",
        404 => "Resource not found.",
        409 => "Conflict.",
        _ => "An error occurred.",
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~EnvelopeResultFilterTests`
Expected: PASS (all cases).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/Http/EnvelopeResultFilter.cs tests/Struo.Tests/Api/EnvelopeResultFilterTests.cs
git commit -m "feat(9a): EnvelopeResultFilter with pure BuildEnvelope rewriting"
```

---

### Task 3: `StruoExceptionHandler.Map` (pure exception → status/code mapping)

The `IExceptionHandler` plus a **pure static** `Map`. Registered nowhere yet; the inline `try/catch` middleware in `Program.cs` still handles exceptions, so the app is unchanged.

**Files:**
- Create: `src/Struo.Api/Http/StruoExceptionHandler.cs`
- Test: `tests/Struo.Tests/Api/StruoExceptionHandlerTests.cs`

**Interfaces:**
- Consumes: `ErrorBody`, `ErrorCodes`, `Envelope` (Task 1); the domain exceptions in `Struo.Domain.Query` (`PermissionDeniedException`, `CollectionNotFoundException`, `RelationConflictException`, `ConcurrencyConflictException`, `QueryException`).
- Produces:
  - `sealed class StruoExceptionHandler : IExceptionHandler`
  - `static (int Status, ErrorBody Body) StruoExceptionHandler.Map(Exception exception, bool authenticated, ILogger? logger = null)`

- [ ] **Step 1: Write the failing test**

`tests/Struo.Tests/Api/StruoExceptionHandlerTests.cs`:
```csharp
using AwesomeAssertions;
using Struo.Api.Http;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Api;

public class StruoExceptionHandlerTests
{
    [Fact]
    public void PermissionDenied_anonymous_is_401_unauthorized()
    {
        var (status, body) = StruoExceptionHandler.Map(new PermissionDeniedException("no"), authenticated: false);
        status.Should().Be(401);
        body.Code.Should().Be("UNAUTHORIZED");
        body.Message.Should().Be("no");
    }

    [Fact]
    public void PermissionDenied_authenticated_is_403_forbidden()
    {
        var (status, body) = StruoExceptionHandler.Map(new PermissionDeniedException("no"), authenticated: true);
        status.Should().Be(403);
        body.Code.Should().Be("FORBIDDEN");
    }

    [Fact]
    public void CollectionNotFound_is_404()
    {
        var (status, body) = StruoExceptionHandler.Map(new CollectionNotFoundException("ghost"), true);
        status.Should().Be(404);
        body.Code.Should().Be("NOT_FOUND");
    }

    [Theory]
    [InlineData(typeof(RelationConflictException))]
    [InlineData(typeof(ConcurrencyConflictException))]
    public void Conflicts_are_409(System.Type exType)
    {
        var ex = (System.Exception)System.Activator.CreateInstance(exType, "c")!;
        var (status, body) = StruoExceptionHandler.Map(ex, true);
        status.Should().Be(409);
        body.Code.Should().Be("CONFLICT");
    }

    [Fact]
    public void QueryException_is_400_bad_user_input()
    {
        var (status, body) = StruoExceptionHandler.Map(new QueryException("bad"), true);
        status.Should().Be(400);
        body.Code.Should().Be("BAD_USER_INPUT");
    }

    [Fact]
    public void Unknown_exception_is_500_masked()
    {
        var (status, body) = StruoExceptionHandler.Map(new System.InvalidOperationException("secret leak"), true);
        status.Should().Be(500);
        body.Code.Should().Be("INTERNAL_SERVER_ERROR");
        body.Message.Should().NotContain("secret");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~StruoExceptionHandlerTests`
Expected: FAIL — `StruoExceptionHandler` does not exist (compile error).

- [ ] **Step 3: Write the implementation**

`src/Struo.Api/Http/StruoExceptionHandler.cs`:
```csharp
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Struo.Domain.Query;

namespace Struo.Api.Http;

/// <summary>
/// Maps StruoCMS domain exceptions to the unified error envelope (mirrors the GraphQL
/// <c>StruoErrorFilter</c> code mapping). Replaces the inline try/catch middleware previously in
/// Program.cs. Unmapped exceptions are masked (no internal detail leaked) and logged server-side.
/// </summary>
public sealed class StruoExceptionHandler(ILogger<StruoExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        if (httpContext.Response.HasStarted) return false;
        var authenticated = httpContext.User.Identity?.IsAuthenticated == true;
        var (status, body) = Map(exception, authenticated, logger);
        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(Envelope.Error(body.Code, body.Message, body.Details), ct);
        return true;
    }

    /// <summary>Pure mapping: exception → (status, ErrorBody). Unmapped → 500 masked (logged if a logger is supplied).</summary>
    public static (int Status, ErrorBody Body) Map(Exception exception, bool authenticated, ILogger? logger = null) =>
        exception switch
        {
            PermissionDeniedException when !authenticated =>
                (StatusCodes.Status401Unauthorized, new ErrorBody(ErrorCodes.Unauthorized, exception.Message)),
            PermissionDeniedException =>
                (StatusCodes.Status403Forbidden, new ErrorBody(ErrorCodes.Forbidden, exception.Message)),
            CollectionNotFoundException =>
                (StatusCodes.Status404NotFound, new ErrorBody(ErrorCodes.NotFound, exception.Message)),
            RelationConflictException or ConcurrencyConflictException =>
                (StatusCodes.Status409Conflict, new ErrorBody(ErrorCodes.Conflict, exception.Message)),
            QueryException =>
                (StatusCodes.Status400BadRequest, new ErrorBody(ErrorCodes.BadUserInput, exception.Message)),
            _ => LogAndMask(exception, logger),
        };

    private static (int, ErrorBody) LogAndMask(Exception exception, ILogger? logger)
    {
        logger?.LogError(exception, "Unhandled API exception");
        return (StatusCodes.Status500InternalServerError,
            new ErrorBody(ErrorCodes.Internal, "An internal error occurred."));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~StruoExceptionHandlerTests`
Expected: PASS (all cases).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/Http/StruoExceptionHandler.cs tests/Struo.Tests/Api/StruoExceptionHandlerTests.cs
git commit -m "feat(9a): StruoExceptionHandler with pure exception->status/code Map"
```

---

### Task 4: Error path goes live (exception handler + validation factory + `Fail` helper)

Wire the exception handler and validation factory, delete the inline middleware, add the `Fail` helper, and convert the hand-written `BadRequest/Unauthorized/Conflict/StatusCode(new{error})` returns in the controllers to `Fail(...)`. **Success returns are NOT changed in this task** (still `new { data }`), so there is no double-wrapping (the filter is not registered yet). After this task, every REST **error** carries `{ success:false, error:{ code, message, details? } }`.

**Files:**
- Create: `src/Struo.Api/Http/ApiResults.cs`
- Modify: `src/Struo.Api/Program.cs` (register handler + `AddProblemDetails` + `UseExceptionHandler`; delete the inline `app.Use(async…try/catch…)` block; set `InvalidModelStateResponseFactory`)
- Modify: `src/Struo.Api/Controllers/AuthController.cs:23`
- Modify: `src/Struo.Api/Controllers/UsersController.cs:26,33,35,37,64`
- Modify: `src/Struo.Api/Controllers/FilesController.cs:32,35`
- Test: `tests/Struo.Tests/Api/ErrorEnvelopeEndpointTests.cs`

**Interfaces:**
- Consumes: `StruoExceptionHandler` (Task 3), `Envelope`/`ErrorBody`/`ValidationDetail`/`ErrorCodes` (Task 1).
- Produces: `static class ApiResults { ObjectResult Fail(int status, string code, string message); }`

- [ ] **Step 1: Write the failing test**

`tests/Struo.Tests/Api/ErrorEnvelopeEndpointTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class ErrorEnvelopeEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    [Fact]
    public async Task Domain_query_exception_is_bad_user_input_envelope()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/items/article?filter[ghost][_eq]=x");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var root = Root(await resp.Content.ReadAsStringAsync());
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
        root.GetProperty("error").GetProperty("message").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Unknown_collection_is_not_found_envelope()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/items/nope");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Root(await resp.Content.ReadAsStringAsync())
            .GetProperty("error").GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Malformed_json_body_is_validation_envelope_with_details()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var content = new StringContent("{ not json", Encoding.UTF8, "application/json");
        var resp = await client.PostAsync("/api/items/article", content);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = Root(await resp.Content.ReadAsStringAsync()).GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("VALIDATION");
        error.GetProperty("details").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Bad_login_is_unauthorized_envelope_via_fail_helper()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(Struo.Api.Auth.CsrfProtectionMiddleware.HeaderName, "1");
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { email = "nope@x.test", password = "wrongwrong" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var root = Root(await resp.Content.ReadAsStringAsync());
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("UNAUTHORIZED");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~ErrorEnvelopeEndpointTests`
Expected: FAIL — bodies lack `success`/`error.code` (the inline middleware emits `{ error:{ message } }`; malformed JSON emits RFC-7807 `ValidationProblemDetails`; bad login lacks `success`/`code`).

- [ ] **Step 3a: Add the `Fail` helper**

`src/Struo.Api/Http/ApiResults.cs`:
```csharp
using Microsoft.AspNetCore.Mvc;

namespace Struo.Api.Http;

public static class ApiResults
{
    /// <summary>
    /// Builds an error-envelope result at the given status. It carries a fully-formed
    /// <see cref="ErrorEnvelope"/>, so the result filter leaves it untouched (idempotent).
    /// </summary>
    public static ObjectResult Fail(int status, string code, string message) =>
        new(Envelope.Error(code, message)) { StatusCode = status };
}
```

- [ ] **Step 3b: Register the exception handler + validation factory; delete the inline middleware**

In `src/Struo.Api/Program.cs`, extend the `AddControllers().AddJsonOptions(...)` chain and add the handler registration. Replace the existing `builder.Services.AddControllers().AddJsonOptions(o => {...});` block with:
```csharp
    builder.Services
        .AddControllers(o => o.Filters.Add<Struo.Api.Http.EnvelopeResultFilter>())   // (filter added in Task 5; harmless here — it only wraps success, and no success return is enveloped manually yet, so leaving it OUT until Task 5 is also fine. If you prefer strict task isolation, omit this line here and add it in Task 5.)
        .AddJsonOptions(o =>
        {
            o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.JsonSerializerOptions.Converters.Add(
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });

    builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(o =>
    {
        o.InvalidModelStateResponseFactory = ctx =>
        {
            var details = ctx.ModelState
                .Where(kv => kv.Value is { Errors.Count: > 0 })
                .SelectMany(kv => kv.Value!.Errors.Select(e =>
                    new Struo.Api.Http.ValidationDetail(
                        kv.Key,
                        string.IsNullOrEmpty(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage)))
                .ToList();
            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(
                Struo.Api.Http.Envelope.Error(Struo.Api.Http.ErrorCodes.Validation,
                    "One or more validation errors occurred.", details));
        };
    });

    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<Struo.Api.Http.StruoExceptionHandler>();
```
> **Note on the filter line:** to keep Task 4 strictly error-only, **omit** `o => o.Filters.Add<…EnvelopeResultFilter>()` here and register it in Task 5 instead (Task 5 Step 3 does exactly that). Either placement passes Task 5's suite; the recommendation is to add the filter in Task 5 so Task 4's commit changes only the error path.

Replace the inline exception middleware block (the whole `app.Use(async (context, next) => { try { await next(); } catch (…) { … } });` in `Program.cs`) with a single line, placed at the same point in the pipeline (immediately after `UseAuthorization()` / before the CSRF + permission middleware, matching where it sat):
```csharp
    app.UseExceptionHandler();
```
> `UseExceptionHandler()` with no argument uses the registered `IExceptionHandler`. Keep it early enough to catch exceptions from the controllers and the CSRF/permission middleware, exactly as the deleted block did (it wrapped `await next()`).

- [ ] **Step 3c: Convert the hand-written controller error returns to `Fail`**

`AuthController.cs` line 23 — replace:
```csharp
            return Unauthorized(new { error = new { message = "Invalid credentials." } });
```
with:
```csharp
            return Struo.Api.Http.ApiResults.Fail(StatusCodes.Status401Unauthorized,
                Struo.Api.Http.ErrorCodes.Unauthorized, "Invalid credentials.");
```

`UsersController.cs` — replace each hand-written error return:
- line 26 (`RequireAdmin`):
```csharp
            : ApiResults.Fail(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, "Admin role required.");
```
- line 33: `return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput, "Email is required.");`
- line 35: `return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput, $"Password must be at least {MinPasswordLength} characters.");`
- line 37: `return ApiResults.Fail(StatusCodes.Status409Conflict, ErrorCodes.Conflict, "Email already in use.");`
- line 51: `return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput, $"Password must be at least {MinPasswordLength} characters.");`
- line 64: `return ApiResults.Fail(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized, "Current password is incorrect.");`

Add `using Struo.Api.Http;` to `UsersController.cs` so `ApiResults`/`ErrorCodes` resolve unqualified.

`FilesController.cs` — add `using Struo.Api.Http;`, then:
- line 32: `return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput, "Expected multipart/form-data.");`
- line 35: `if (file is null) return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput, "Missing 'file' part.");`

> Leave all `NotFound()` / `NoContent()` returns as-is. Leave all success returns (`Ok(new { data })`, `Created(..., new { data })`, `StatusCode(201, new { data })`) untouched in this task — they are simplified in Task 5.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~ErrorEnvelopeEndpointTests`
Expected: PASS (4 cases).
Then run the RBAC/Users/Auth suites that assert error paths to confirm nothing regressed:
Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~RbacEnforcementTests|FullyQualifiedName~UsersControllerRbacTests|FullyQualifiedName~CsrfProtectionTests"`
Expected: PASS (statuses unchanged; bodies now enveloped — these suites assert on status codes, which are preserved).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/Http/ApiResults.cs src/Struo.Api/Program.cs src/Struo.Api/Controllers/AuthController.cs src/Struo.Api/Controllers/UsersController.cs src/Struo.Api/Controllers/FilesController.cs tests/Struo.Tests/Api/ErrorEnvelopeEndpointTests.cs
git commit -m "feat(9a): error path goes live — exception handler, validation factory, Fail helper"
```

---

### Task 5: Success path goes live (register filter + simplify controller success returns)

Register `EnvelopeResultFilter` globally and simplify every controller's **success** return to raw data / a `PagedResult`. After this task the whole REST surface is enveloped. This is the atomic success switch-over: the filter and the controller simplifications land together (filter without simplification → double-wrap; simplification without filter → un-enveloped), so the task gate is the full suite green.

**Files:**
- Modify: `src/Struo.Api/Program.cs` (add `o => o.Filters.Add<EnvelopeResultFilter>()` to `AddControllers`, if not already added in Task 4)
- Modify: `src/Struo.Api/Controllers/ItemsController.cs:23,32,42,83,91,109`
- Modify: `src/Struo.Api/Controllers/AuthController.cs:28,59-67`
- Modify: `src/Struo.Api/Controllers/UsersController.cs:44,83`
- Modify: `src/Struo.Api/Controllers/FilesController.cs:39-46,55-62`
- Modify: `src/Struo.Api/Controllers/LanguagesController.cs:19`
- Modify: `src/Struo.Api/Controllers/SchemaController.cs` (already raw — no change needed, verify)
- Modify: `src/Struo.Api/Controllers/PingController.cs` (already raw — no change needed, verify)
- Modify (test fix): `tests/Struo.Tests/Api/SchemaEndpointTests.cs` (the JsonDocument `GetProperty("fields")` case)
- Test: `tests/Struo.Tests/Api/SuccessEnvelopeEndpointTests.cs`

**Interfaces:**
- Consumes: `EnvelopeResultFilter` (Task 2), `PagedResult` (Task 1).
- Produces: nothing new (controllers now emit raw values; the filter wraps them).

- [ ] **Step 1: Write the failing test**

`tests/Struo.Tests/Api/SuccessEnvelopeEndpointTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class SuccessEnvelopeEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    [Fact]
    public async Task List_has_success_data_and_meta()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var resp = await client.GetAsync("/api/items/article?limit=2&offset=0");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var root = Root(await resp.Content.ReadAsStringAsync());
        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Array);
        var meta = root.GetProperty("meta");
        meta.GetProperty("total").GetInt64().Should().BeGreaterThanOrEqualTo(0);
        meta.TryGetProperty("limit", out _).Should().BeTrue();
        meta.TryGetProperty("offset", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Create_get_are_success_data_without_meta()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var create = await client.PostAsJsonAsync("/api/items/article",
            new { status = "draft", translations = new { en = new { title = "EnvOk" } } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = Root(await create.Content.ReadAsStringAsync());
        created.GetProperty("success").GetBoolean().Should().BeTrue();
        created.TryGetProperty("meta", out _).Should().BeFalse();
        var id = created.GetProperty("data").GetProperty("id").GetString()!;

        var get = await client.GetAsync($"/api/items/article/{id}");
        var got = Root(await get.Content.ReadAsStringAsync());
        got.GetProperty("success").GetBoolean().Should().BeTrue();
        got.GetProperty("data").GetProperty("id").GetString().Should().Be(id);
    }

    [Fact]
    public async Task Delete_stays_bare_204()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var create = await client.PostAsJsonAsync("/api/items/article",
            new { status = "draft", translations = new { en = new { title = "ToDelete" } } });
        var id = Root(await create.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
        var del = await client.DeleteAsync($"/api/items/article/{id}?purge=true");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await del.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task File_content_is_not_enveloped()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        using var form = new MultipartFormDataContent();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(part, "file", "blob.bin");
        var upload = await client.PostAsync("/api/files", form);
        upload.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = Root(await upload.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;

        var content = await client.GetAsync($"/api/files/{id}/content");
        content.StatusCode.Should().Be(HttpStatusCode.OK); // local storage streams the bytes (no presigned redirect)
        (await content.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~SuccessEnvelopeEndpointTests`
Expected: FAIL — responses lack `success` (controllers still emit `new { data }` / raw); `List_has_success_data_and_meta` fails on `GetProperty("success")`.

- [ ] **Step 3a: Register the filter**

In `src/Struo.Api/Program.cs`, ensure `AddControllers` registers the filter:
```csharp
    builder.Services
        .AddControllers(o => o.Filters.Add<Struo.Api.Http.EnvelopeResultFilter>())
        .AddJsonOptions(o => { /* unchanged camelCase + enum converter */ });
```

- [ ] **Step 3b: Simplify `ItemsController` success returns**

Replace the enveloped success returns:
- line 23 (`List`):
```csharp
        return Ok(new Struo.Api.Http.PagedResult(result.Data, result.Total, result.Limit, result.Offset));
```
- line 32 (`Query`): same as `List` —
```csharp
        return Ok(new Struo.Api.Http.PagedResult(result.Data, result.Total, result.Limit, result.Offset));
```
- line 42 (`Get`): `return item is null ? NotFound() : Ok(item);`
- line 83 (`Create`): `return Created($"/api/items/{collection}/{id}", created);`
- line 91 (`Update`): `return updated is null ? NotFound() : Ok(updated);`
- line 109 (`Restore`): `return restored is null ? NotFound() : Ok(restored);`

> `result.Data`/`.Total`/`.Limit`/`.Offset` are the same members the current inline `new { data = result.Data, meta = new { total = result.Total, … } }` already reads — confirm the field names against `ItemService.QueryAsync`'s return type when editing.

- [ ] **Step 3c: Simplify `AuthController` success returns**

- line 28 (`Login`): `return Ok(new { id = result.UserId });`
- lines 59-67 (`Me`): replace `return Ok(new { data = new { … } });` with the inner object directly:
```csharp
        return Ok(new
        {
            id = User.FindFirstValue(ClaimTypes.NameIdentifier),
            isSuperAdmin = eff.IsSuperAdmin,
            permissions = map
        });
```

- [ ] **Step 3d: Simplify `UsersController` success returns**

- line 44 (`Create`): `return Created($"/api/items/user/{id}", new { id, email = body.Email, name = body.Name });`
- line 83 (`GenerateToken`): `return Ok(new { token }); // shown once`

- [ ] **Step 3e: Simplify `FilesController` success returns**

- lines 39-46 (`Upload`): replace `return StatusCode(StatusCodes.Status201Created, new { data = new { … } });` with:
```csharp
        return StatusCode(StatusCodes.Status201Created, new
        {
            id = created.Id, fileName = created.FileName, contentType = created.ContentType,
            size = created.Size, width = created.Width, height = created.Height, status = created.Status
        });
```
- lines 55-62 (`Get`): replace `return Ok(new { data = new { … } });` with:
```csharp
        return Ok(new
        {
            id = row.Id, fileName = row.FileName, contentType = row.ContentType,
            size = row.Size, width = row.Width, height = row.Height, status = row.Status
        });
```
> Copy the exact property set from the current `Get` body (`id, fileName, contentType, size, width, height, status`) — do not alter it. Leave `Download` (`Redirect`/`File`) and both `NotFound()` returns untouched.

- [ ] **Step 3f: Simplify `LanguagesController` success return**

- line 19: replace `return Ok(new { data });` with `return Ok(data);`

- [ ] **Step 3g: Verify `SchemaController` / `PingController` (no code change)**

Both already return raw values (`Ok(schema.GetAll())`, `Ok(meta)`, `Ok(new { status, service, utc })`), so the filter now wraps them into `{ success:true, data:… }` automatically. No edit needed — confirm by inspection.

- [ ] **Step 3h: Fix the one coupled existing test**

`tests/Struo.Tests/Api/SchemaEndpointTests.cs`, in `Schema_for_collection_includes_interfaces_options_and_seo`, the response root is now the envelope. Change:
```csharp
        var fields = doc.RootElement.GetProperty("fields");
```
to:
```csharp
        var fields = doc.RootElement.GetProperty("data").GetProperty("fields");
```
> The substring assertions in that file (and the Ping smoke test) still pass — the enveloped body still contains `"name":"article"`, `"status":"ok"`, etc.

- [ ] **Step 4: Run the full backend suite**

Run: `dotnet build -warnaserror` then `dotnet test tests/Struo.Tests`
Expected: build 0 warnings; **all tests pass** (599 baseline + Task 1-5 new tests; only `SchemaEndpointTests` needed the one-line fix). If any other test fails, it asserted a raw top-level shape — update it to read under `data`/`meta`/`error` (do NOT change controller behaviour to satisfy a stale assertion).

- [ ] **Step 5: Verify no manual envelopes remain, then commit**

Run: `grep -rnE "new \{ (data|error) =" src/Struo.Api/Controllers/`
Expected: **no matches** (all manual `{ data }`/`{ error }` are gone).
```bash
git add src/Struo.Api/Program.cs src/Struo.Api/Controllers/ tests/Struo.Tests/Api/SchemaEndpointTests.cs tests/Struo.Tests/Api/SuccessEnvelopeEndpointTests.cs
git commit -m "feat(9a): success path goes live — register filter, simplify controllers"
```

---

### Task 6: Documentation

Add the envelope contract to the guide so consumers (and 9a-fe) have one authoritative reference. No code change.

**Files:**
- Create: `docs/guide/04-api-response-envelope.md`
- Modify: `docs/ROADMAP.md` (add the Phase 9a row / status line, marked done-pending-live until Task 7 passes)

- [ ] **Step 1: Write the guide page**

`docs/guide/04-api-response-envelope.md` — document: the success shape `{ success, data, meta? }` (meta only on lists, offset-based), the error shape `{ success, error:{ code, message, details? } }`, the full code taxonomy table (§4 of the spec), the not-enveloped cases (204, binary, redirect), and a one-line note that GraphQL keeps `{ data, errors }`. Include a `curl` example of each.

- [ ] **Step 2: Add the ROADMAP entry**

Add a Phase 9a bullet + table row to `docs/ROADMAP.md` mirroring the 9b entries' style, status "done (live-gate pending)" until Task 7. Note the frontend deferral to 9a-fe.

- [ ] **Step 3: Commit**

```bash
git add docs/guide/04-api-response-envelope.md docs/ROADMAP.md
git commit -m "docs(9a): API response envelope guide + roadmap entry"
```

---

### Task 7: Verification & live gate

Prove the slice end-to-end: full automated gates, the frontend compatibility gate (guards the 9a-fe deferral), and the real-Postgres live gate (§17.2). No new production code — fixes only if the gate surfaces a defect.

**Files:**
- (fixes only, if the live gate finds a defect)

- [ ] **Step 1: Backend automated gate**

Run: `dotnet build -warnaserror` (expect 0 warnings) then `dotnet test tests/Struo.Tests` (expect all green).

- [ ] **Step 2: Frontend compatibility gate (guards the 9a-fe deferral)**

Run: `cd frontend && pnpm test`
Expected: **261/261 green, no frontend code changed** — proves the existing SPA is still compatible with the new envelope. Then `pnpm build` (expect success).

- [ ] **Step 3: Live gate on real Postgres (§17.2)**

Bring up the dev API against live Postgres (memory: set `ASPNETCORE_URLS=http://localhost:5080`, real PG via `appsettings.Development.json`, bootstrap admin). Use PowerShell `Invoke-RestMethod` (memory: not Git Bash curl for non-ASCII). Verify each — record the actual response body:

1. `GET /api/items/article?limit=2` → `{ success:true, data:[…], meta:{ total, limit, offset } }`.
2. `GET /api/items/article/{id}` → `{ success:true, data:{…} }` (no `meta`); unknown id → 404 `{ success:false, error:{ code:"NOT_FOUND" } }`.
3. `POST /api/items/article` (valid en title) → 201 `{ success:true, data:{…} }`; malformed JSON body → 400 `{ success:false, error:{ code:"VALIDATION", details:[…] } }`.
4. `GET /api/items/article?deleted=banana` → 400 `{ code:"BAD_USER_INPUT" }`.
5. Anonymous read of a non-public collection → 401 `{ code:"UNAUTHORIZED" }`; authenticated-but-denied → 403 `{ code:"FORBIDDEN" }`.
6. A `CONFLICT` path (stale `version` on an update, or an inbound-Restrict delete) → 409 `{ code:"CONFLICT" }`.
7. `GET /api/files/{id}/content` → still **302** (presigned S3/MinIO) or a binary stream — body is **not** an envelope.
8. `DELETE /api/items/article/{id}` (or `?purge=true`) success → **204** with empty body.
9. Any CJK message in an error/data round-trips code-point-exact.
10. `GET /api/schema` and `GET /api/schema/article` → now `{ success:true, data:… }` (previously raw).

- [ ] **Step 4: If the live gate surfaces a Postgres-only defect**

Fix it (Struo.Api only), add a regression test, re-run Steps 1-3. Follow the "SQLite-green ≠ Postgres-correct" discipline (memory `db-verify-live-postgres`).

- [ ] **Step 5: Finalize the ROADMAP entry**

Update the Phase 9a ROADMAP row to "done & live-verified (real PG)" with the gate results (N/N), the verification baseline (backend test count, frontend 261), and any live-gate fix commit hash.

```bash
git add docs/ROADMAP.md
git commit -m "docs(9a): roadmap — unified response envelope done & live-verified (real PG)"
```

---

## Self-Review

**Spec coverage:**
- Envelope contract (§2) → Task 1 (types/factory) + Task 5 (success wire) + Task 4 (error wire). ✅
- `meta` offset-based, list-only (§2) → Task 1 `MetaInfo`/`PagedResult`, Task 5 `List`/`Query`. ✅
- 204 bare / binary / redirect not enveloped (§3) → Task 2 filter cases + Task 5 file-content test. ✅
- Error code taxonomy (§4) → Task 1 `ErrorCodes`, Task 3 `Map`, Task 4 factory (`VALIDATION`). ✅
- Result filter (§5.2) → Task 2. Exception handler (§5.3) → Task 3 + Task 4 wiring. Validation factory (§5.4) → Task 4. Controller simplification + `Fail` (§5.5) → Task 4 (errors) + Task 5 (success). ✅
- Frontend untouched + compatibility guarded (§6) → Task 7 Step 2. ✅
- Testing strategy (§7): filter/handler unit → Tasks 2/3; validation → Task 4; integration (list/get/create/validation/domain/404/file) → Tasks 4+5; compatibility gate + live gate → Task 7. ✅
- Out of scope (§9): no Domain/App/Infra edits, no packages, no migration, GraphQL untouched → Global Constraints; every task touches `Struo.Api`/tests/docs only. ✅

**Placeholder scan:** No TBD/TODO. Every code step shows complete code. The one intentional `> Note` about where to register the filter is a real instruction (recommends Task 5 placement), not a placeholder.

**Type consistency:** `Envelope.Success`/`Envelope.Error`, `SuccessEnvelope`/`ErrorEnvelope`/`ErrorBody`/`ValidationDetail`/`MetaInfo`/`PagedResult`, `ErrorCodes.ForStatus`/constants, `EnvelopeResultFilter.BuildEnvelope`, `StruoExceptionHandler.Map`, `ApiResults.Fail` — names are consistent across Tasks 1-5. `result.Data/.Total/.Limit/.Offset` flagged for confirmation against `ItemService.QueryAsync`'s return type at edit time (Task 5 Step 3b note).

**`FilesController.Get`:** the Task 5 Step 3e snippet copies the current property set verbatim (`id, fileName, contentType, size, width, height, status`) — no field change, only the `data =` wrapper is dropped.
