using AwesomeAssertions;
using Struo.Api.Http;
using Struo.Application.Security;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// The single source of truth shared by REST (<see cref="StruoExceptionHandler"/>) and
/// GraphQL (<c>StruoErrorFilter</c>). One exception ⇒ one code, on both protocols.
/// </summary>
public class DomainErrorMapTests
{
    [Fact]
    public void PermissionDenied_unauthenticated_is_UNAUTHORIZED_with_message()
    {
        var (code, message) = DomainErrorMap.Map(new PermissionDeniedException("nope"), authenticated: false);
        code.Should().Be("UNAUTHORIZED");
        message.Should().Be("nope");
    }

    [Fact]
    public void PermissionDenied_authenticated_is_FORBIDDEN_with_message()
    {
        var (code, message) = DomainErrorMap.Map(new PermissionDeniedException("nope"), authenticated: true);
        code.Should().Be("FORBIDDEN");
        message.Should().Be("nope");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CollectionNotFound_is_NOT_FOUND_regardless_of_auth(bool authenticated)
    {
        var (code, message) = DomainErrorMap.Map(new CollectionNotFoundException("ghost"), authenticated);
        code.Should().Be("NOT_FOUND");
        message.Should().Contain("ghost");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RelationConflict_is_CONFLICT_regardless_of_auth(bool authenticated)
    {
        var (code, message) = DomainErrorMap.Map(new RelationConflictException("rel"), authenticated);
        code.Should().Be("CONFLICT");
        message.Should().Be("rel");
    }

    // Optimistic-lock (CAS miss) is split out from generic CONFLICT so the frontend's
    // "changed by someone else" recovery banner keys on VERSION_CONFLICT alone and is not tripped
    // by any other 409 (e.g. duplicate email). RelationConflict above stays CONFLICT.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConcurrencyConflict_is_VERSION_CONFLICT_regardless_of_auth(bool authenticated)
    {
        var (code, message) = DomainErrorMap.Map(new ConcurrencyConflictException("stale"), authenticated);
        code.Should().Be("VERSION_CONFLICT");
        message.Should().Be("stale");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void QueryException_is_BAD_USER_INPUT_regardless_of_auth(bool authenticated)
    {
        var (code, message) = DomainErrorMap.Map(new QueryException("bad"), authenticated);
        code.Should().Be("BAD_USER_INPUT");
        message.Should().Be("bad");
    }

    // A lying/streaming upload whose actual bytes exceed MaxUploadBytes must map to a
    // dedicated 413, distinct from QueryException's generic 400 BAD_USER_INPUT above.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PayloadTooLarge_is_PAYLOAD_TOO_LARGE_regardless_of_auth(bool authenticated)
    {
        var (code, message) = DomainErrorMap.Map(new PayloadTooLargeException("too big"), authenticated);
        code.Should().Be("PAYLOAD_TOO_LARGE");
        message.Should().Be("too big");
    }

    // Distinguishable from a plain unmapped/masked failure: the password/user-delete write already
    // succeeded, so the caller (and the SPA) need a signal that specifically means "the write went
    // through, but some sessions might still be alive" rather than the generic masked 500.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SessionRevocationFailed_is_its_own_code_with_the_client_safe_message(bool authenticated)
    {
        var exception = new SessionRevocationFailedException(
            "Password was changed, but revoking existing sessions failed.",
            new InvalidOperationException("db unreachable"));
        var (code, message) = DomainErrorMap.Map(exception, authenticated);
        code.Should().Be("SESSION_REVOCATION_FAILED");
        message.Should().Be("Password was changed, but revoking existing sessions failed.");
        message.Should().NotContain("db unreachable");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Unmapped_exception_is_INTERNAL_and_masked(bool authenticated)
    {
        var (code, message) = DomainErrorMap.Map(new InvalidOperationException("secret leak"), authenticated);
        code.Should().Be("INTERNAL_SERVER_ERROR");
        message.Should().Be("An internal error occurred.");
        message.Should().NotContain("secret");
    }

    [Theory]
    [InlineData("UNAUTHORIZED", 401)]
    [InlineData("FORBIDDEN", 403)]
    [InlineData("NOT_FOUND", 404)]
    [InlineData("CONFLICT", 409)]
    [InlineData("VERSION_CONFLICT", 409)]
    [InlineData("BAD_USER_INPUT", 400)]
    [InlineData("VALIDATION", 400)]
    [InlineData("PAYLOAD_TOO_LARGE", 413)]
    [InlineData("INTERNAL_SERVER_ERROR", 500)]
    [InlineData("SESSION_REVOCATION_FAILED", 500)]
    public void StatusFor_maps_each_code_to_its_http_status(string code, int expected)
        => DomainErrorMap.StatusFor(code).Should().Be(expected);

    // The status→code reverse map (used by inline ApiResults.Fail, e.g. UsersController
    // duplicate-email, which does NOT flow through DomainErrorMap) must keep answering the generic
    // Conflict for 409 — the VERSION_CONFLICT split is exception-driven only.
    [Fact]
    public void ForStatus_409_stays_generic_CONFLICT()
        => ErrorCodes.ForStatus(409).Should().Be("CONFLICT");

    [Fact]
    public void ForStatus_413_is_PAYLOAD_TOO_LARGE()
        => ErrorCodes.ForStatus(413).Should().Be("PAYLOAD_TOO_LARGE");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SearchUnavailable_is_SEARCH_UNAVAILABLE_with_a_fixed_message_regardless_of_auth(bool authenticated)
    {
        var (code, message) = DomainErrorMap.Map(new SearchUnavailableException("meilisearch at 10.0.0.5:7700 refused"), authenticated);
        code.Should().Be("SEARCH_UNAVAILABLE");
        message.Should().Be("Search is temporarily unavailable.");
        message.Should().NotContain("10.0.0.5", "the provider's own message must never reach a client");
    }

    [Fact]
    public void SEARCH_UNAVAILABLE_is_503_in_both_directions()
    {
        DomainErrorMap.StatusFor("SEARCH_UNAVAILABLE").Should().Be(503);
        ErrorCodes.ForStatus(503).Should().Be("SEARCH_UNAVAILABLE");
    }
}
