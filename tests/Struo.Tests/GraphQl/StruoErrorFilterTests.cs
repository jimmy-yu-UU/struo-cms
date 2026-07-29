// tests/Struo.Tests/GraphQl/StruoErrorFilterTests.cs
using System.Security.Claims;
using AwesomeAssertions;
using HotChocolate;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Struo.Api.GraphQl;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.GraphQl;

public class StruoErrorFilterTests
{
    private static IError Wrap(Exception ex) =>
        ErrorBuilder.New().SetMessage("original").SetException(ex).Build();

    // No HttpContext ⇒ unauthenticated (non-HTTP execution path).
    private static StruoErrorFilter FilterAnonymous() =>
        new(NullLoggerFactory.Instance.CreateLogger<StruoErrorFilter>(),
            new HttpContextAccessor { HttpContext = null });

    // An authenticated HttpContext (a ClaimsPrincipal whose identity IsAuthenticated).
    private static StruoErrorFilter FilterAuthenticated()
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Test"))
        };
        return new(NullLoggerFactory.Instance.CreateLogger<StruoErrorFilter>(),
            new HttpContextAccessor { HttpContext = ctx });
    }

    private readonly StruoErrorFilter _filter = FilterAnonymous();

    [Fact]
    public void QueryException_maps_to_BAD_USER_INPUT()
    {
        var e = _filter.OnError(Wrap(new QueryException("bad")));
        e.Code.Should().Be("BAD_USER_INPUT");
        e.Message.Should().Be("bad");
    }

    [Fact]
    public void CollectionNotFound_maps_to_NOT_FOUND()
        => _filter.OnError(Wrap(new CollectionNotFoundException("x"))).Code.Should().Be("NOT_FOUND");

    // Drift fix: with no HttpContext (unauthenticated), PermissionDenied is UNAUTHORIZED,
    // matching REST semantics — not the old unconditional FORBIDDEN.
    [Fact]
    public void PermissionDenied_when_unauthenticated_maps_to_UNAUTHORIZED()
    {
        var e = FilterAnonymous().OnError(Wrap(new PermissionDeniedException("no")));
        e.Code.Should().Be("UNAUTHORIZED");
        e.Message.Should().Be("no");
    }

    [Fact]
    public void PermissionDenied_when_authenticated_maps_to_FORBIDDEN()
    {
        var e = FilterAuthenticated().OnError(Wrap(new PermissionDeniedException("no")));
        e.Code.Should().Be("FORBIDDEN");
        e.Message.Should().Be("no");
    }

    [Fact]
    public void Conflict_maps_to_CONFLICT()
        => _filter.OnError(Wrap(new RelationConflictException("c"))).Code.Should().Be("CONFLICT");

    // Optimistic-lock CAS miss splits to VERSION_CONFLICT over GraphQL too (shared DomainErrorMap).
    [Fact]
    public void ConcurrencyConflict_maps_to_VERSION_CONFLICT()
        => _filter.OnError(Wrap(new ConcurrencyConflictException("c"))).Code.Should().Be("VERSION_CONFLICT");

    [Fact]
    public void Unknown_exception_is_masked_as_INTERNAL_SERVER_ERROR()
    {
        var e = _filter.OnError(Wrap(new InvalidOperationException("secret detail")));
        e.Code.Should().Be("INTERNAL_SERVER_ERROR");
        e.Message.Should().NotContain("secret");
    }

    [Fact]
    public void Null_exception_error_is_returned_unchanged()
    {
        var validationError = ErrorBuilder.New().SetMessage("parse error").Build();
        _filter.OnError(validationError).Should().BeSameAs(validationError);
    }
}
