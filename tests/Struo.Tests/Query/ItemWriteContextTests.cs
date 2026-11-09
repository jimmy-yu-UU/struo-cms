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
    public void Public_surface_has_only_the_documented_members()
    {
        typeof(ItemWriteContext).GetProperties().Select(p => p.Name).Where(n => n != "EqualityContract")
            .Should().BeEquivalentTo("Operation", "Meta", "Entity", "Id", "Existing", "BodyKeys", "Changed", "Body");
        typeof(ItemWriteContext).GetMethod("Deconstruct").Should().BeNull();
    }
}
