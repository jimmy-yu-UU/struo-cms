using AwesomeAssertions;
using Struo.Domain.Metadata;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

public sealed class KeyLookupTests
{
    private sealed class Holder
    {
        public Guid OwnerId { get; set; }
    }

    [Fact]
    public void Require_returns_the_property_with_the_exact_name()
    {
        KeyLookup.Require(typeof(Holder), "OwnerId", caseSensitive: true).Name.Should().Be("OwnerId");
    }

    [Fact]
    public void Require_matches_another_letter_case_only_when_asked_to()
    {
        KeyLookup.Require(typeof(Holder), "ownerid", caseSensitive: false).Name.Should().Be("OwnerId");

        var exact = () => KeyLookup.Require(typeof(Holder), "ownerid", caseSensitive: true);

        exact.Should().Throw<MetadataException>().Which.Message.Should().Contain("Holder has no property 'ownerid'; it has 'OwnerId'");
    }

    [Fact]
    public void Require_refuses_a_name_that_matches_no_property()
    {
        var missing = () => KeyLookup.Require(typeof(Holder), "NoSuchKey", caseSensitive: true);

        missing.Should().Throw<MetadataException>().Which.Message.Should().Contain("Holder has no property with that name");
    }
}
