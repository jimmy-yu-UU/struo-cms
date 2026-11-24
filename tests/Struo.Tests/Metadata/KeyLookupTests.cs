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
        KeyLookup.Require(typeof(Holder), "OwnerId").Name.Should().Be("OwnerId");
    }

    [Fact]
    public void Require_refuses_another_letter_case_and_names_the_property_it_misses()
    {
        var exact = () => KeyLookup.Require(typeof(Holder), "ownerid");

        exact.Should().Throw<MetadataException>().Which.Message.Should().Contain("Holder has no property 'ownerid'; it has 'OwnerId'");
    }

    [Fact]
    public void Require_refuses_a_name_that_matches_no_property()
    {
        var missing = () => KeyLookup.Require(typeof(Holder), "NoSuchKey");

        missing.Should().Throw<MetadataException>().Which.Message.Should().Contain("Holder has no property with that name");
    }

    [Fact]
    public void Absence_names_what_the_name_stands_for()
    {
        KeyLookup.Absence(typeof(Holder), "Sort", "sort field").Should()
            .Contain("that holds the sort field, or add 'Sort' to Holder");
        KeyLookup.Absence(typeof(Holder), "ownerid", "sort field").Should()
            .Contain("sort field names are case-sensitive. Write the sort field as 'OwnerId'");
    }

    [Fact]
    public void HasNoColumn_is_true_only_for_a_property_marked_IsIgnore()
    {
        KeyLookup.HasNoColumn(typeof(WithIgnored).GetProperty(nameof(WithIgnored.Ignored))!).Should().BeTrue();
        KeyLookup.HasNoColumn(typeof(WithIgnored).GetProperty(nameof(WithIgnored.Stored))!).Should().BeFalse();
    }

    // SqlSugar reads [SugarColumn] off the property it maps, without inheritance: an override that does not
    // repeat IsIgnore has a column, and an override that sets it has none.
    [Fact]
    public void HasNoColumn_reads_the_attribute_off_the_overriding_property_only()
    {
        KeyLookup.HasNoColumn(typeof(OverrideWithoutTheAttribute).GetProperty(nameof(OverrideWithoutTheAttribute.Key))!)
            .Should().BeFalse();
        KeyLookup.HasNoColumn(typeof(OverrideThatIgnores).GetProperty(nameof(OverrideThatIgnores.Key))!)
            .Should().BeTrue();
    }

    private abstract class IgnoredOnTheBase
    {
        [SqlSugar.SugarColumn(IsIgnore = true)] public virtual int Key { get; set; }
    }

    private sealed class OverrideWithoutTheAttribute : IgnoredOnTheBase
    {
        public override int Key { get; set; }
    }

    private abstract class PlainOnTheBase
    {
        public virtual int Key { get; set; }
    }

    private sealed class OverrideThatIgnores : PlainOnTheBase
    {
        [SqlSugar.SugarColumn(IsIgnore = true)] public override int Key { get; set; }
    }

    private sealed class WithIgnored
    {
        [SqlSugar.SugarColumn(IsIgnore = true)] public int Ignored { get; set; }
        public int Stored { get; set; }
    }
}
