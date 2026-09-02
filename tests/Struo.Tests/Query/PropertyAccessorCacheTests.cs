using AwesomeAssertions;
using Struo.Application.Query;
using Xunit;

namespace Struo.Tests.Query;

public class PropertyAccessorCacheTests
{
    private sealed class Sample
    {
        public Guid AuthorId { get; set; }
        public string? Title { get; set; }
    }

    [Fact]
    public void Resolve_returns_the_same_PropertyInfo_reference_on_repeat()
    {
        var first = PropertyAccessorCache.Resolve(typeof(Sample), "AuthorId");
        var second = PropertyAccessorCache.Resolve(typeof(Sample), "AuthorId");

        first.Should().NotBeNull();
        second.Should().BeSameAs(first);
    }

    [Fact]
    public void Resolve_is_case_insensitive()
    {
        var lower = PropertyAccessorCache.Resolve(typeof(Sample), "authorId");
        var exact = PropertyAccessorCache.Resolve(typeof(Sample), "AuthorId");

        lower.Should().NotBeNull();
        lower!.Name.Should().Be(exact!.Name);
    }

    [Fact]
    public void Read_returns_null_for_a_missing_property()
    {
        PropertyAccessorCache.Read(new Sample(), "Nope").Should().BeNull();
    }

    [Fact]
    public void Read_returns_the_property_value()
    {
        var id = Guid.NewGuid();
        PropertyAccessorCache.Read(new Sample { AuthorId = id }, "authorId").Should().Be(id);
    }

    [Fact]
    public void Read_returns_null_for_an_unset_string_property()
    {
        PropertyAccessorCache.Read(new Sample(), "title").Should().BeNull();
    }
}
