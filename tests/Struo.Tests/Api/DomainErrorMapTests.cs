using AwesomeAssertions;
using Struo.Api.Http;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// ARC-3: the single source of truth shared by REST (<see cref="StruoExceptionHandler"/>) and
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConcurrencyConflict_is_CONFLICT_regardless_of_auth(bool authenticated)
    {
        var (code, message) = DomainErrorMap.Map(new ConcurrencyConflictException("stale"), authenticated);
        code.Should().Be("CONFLICT");
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
    [InlineData("BAD_USER_INPUT", 400)]
    [InlineData("VALIDATION", 400)]
    [InlineData("INTERNAL_SERVER_ERROR", 500)]
    public void StatusFor_maps_each_code_to_its_http_status(string code, int expected)
        => DomainErrorMap.StatusFor(code).Should().Be(expected);
}
