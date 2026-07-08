// tests/Struo.Tests/GraphQl/StruoErrorFilterTests.cs
using AwesomeAssertions;
using HotChocolate;
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

    private readonly StruoErrorFilter _filter = new(NullLoggerFactory.Instance.CreateLogger<StruoErrorFilter>());

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

    [Fact]
    public void PermissionDenied_maps_to_FORBIDDEN()
        => _filter.OnError(Wrap(new PermissionDeniedException("no"))).Code.Should().Be("FORBIDDEN");

    [Fact]
    public void Conflict_maps_to_CONFLICT()
        => _filter.OnError(Wrap(new RelationConflictException("c"))).Code.Should().Be("CONFLICT");

    [Fact]
    public void ConcurrencyConflict_also_maps_to_CONFLICT()
        => _filter.OnError(Wrap(new ConcurrencyConflictException("c"))).Code.Should().Be("CONFLICT");

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
