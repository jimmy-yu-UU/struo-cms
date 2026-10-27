// tests/Struo.Tests/Query/ItemWriteContextTests.cs
using AwesomeAssertions;
using Struo.Application.Query;
using Struo.Domain.Metadata.Models;
using Xunit;

namespace Struo.Tests.Query;

public class ItemWriteContextTests
{
    private static ItemWriteContext NewContext() => new(
        ItemWriteOperation.Trash,
        new CollectionMetadata { Name = "x", Label = "X", FieldGroups = [], Fields = [] },
        new object(), "1", new HashSet<string>(), null);

    [Fact]
    public void Changed_defaults_to_true()
    {
        NewContext().Changed.Should().BeTrue();
    }

    [Fact]
    public void A_fork_test_can_build_a_context_whose_write_changed_nothing()
    {
        var context = NewContext();
        context.Changed = false;

        context.Changed.Should().BeFalse();
    }
}
