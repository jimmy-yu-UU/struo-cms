// tests/Struo.Tests/Query/QuerySpecDefaultsTests.cs
using AwesomeAssertions;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

public sealed class QuerySpecDefaultsTests
{
    [Fact]
    public void A_deep_relation_spec_built_without_a_nested_spec_has_no_nested_spec()
    {
        var spec = new DeepRelationSpec(null, null);

        spec.Deep.Should().BeNull();
    }

    [Fact]
    public void The_default_deleted_filter_excludes_trashed_rows()
    {
        default(DeletedFilter).Should().Be(DeletedFilter.Exclude);
    }
}
