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

    [Fact]
    public void RelationConflict_is_409_CONFLICT()
    {
        var (status, body) = StruoExceptionHandler.Map(new RelationConflictException("c"), true);
        status.Should().Be(409);
        body.Code.Should().Be("CONFLICT");
    }

    // Optimistic-lock CAS miss splits to VERSION_CONFLICT (still 409) over REST.
    [Fact]
    public void ConcurrencyConflict_is_409_VERSION_CONFLICT()
    {
        var (status, body) = StruoExceptionHandler.Map(new ConcurrencyConflictException("stale"), true);
        status.Should().Be(409);
        body.Code.Should().Be("VERSION_CONFLICT");
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
