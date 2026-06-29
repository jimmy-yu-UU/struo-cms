using AwesomeAssertions;
using Struo.Application.Query;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

public class IdParsingTests
{
    [Fact]
    public void ParseTo_returns_guid_for_guid_target()
    {
        var g = Guid.CreateVersion7();
        IdParsing.ParseTo(g.ToString(), typeof(Guid)).Should().BeOfType<Guid>().And.Be(g);
    }

    [Fact]
    public void ParseTo_returns_guid_for_nullable_guid_target()
    {
        var g = Guid.CreateVersion7();
        IdParsing.ParseTo(g.ToString(), typeof(Guid?)).Should().Be(g);
    }

    [Fact]
    public void ParseTo_returns_long_for_long_target()
    {
        IdParsing.ParseTo("42", typeof(long)).Should().Be(42L);
    }

    [Fact]
    public void ParseTo_throws_QueryException_for_malformed_id()
    {
        var act = () => IdParsing.ParseTo("not-a-guid", typeof(Guid));
        act.Should().Throw<QueryException>();
    }
}
