// tests/Struo.Tests/Query/FieldValueRulesTests.cs
using AwesomeAssertions;
using Struo.Application.Query;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// Unit tests for <see cref="FieldValueRules.IsMissing"/> — the single predicate <see
/// cref="ItemDeserializer"/> and the per-locale translation validators share to decide whether a
/// Required field's value counts as present. <see cref="Guid.Empty"/> is the one non-string,
/// non-null value treated as missing (otherwise a non-nullable Guid FK with Required=true has no
/// way to ever be "missing" — omitting it from a JSON body just leaves the CLR default). No other
/// value type gets this treatment: a falsy scalar like <c>0</c> stays legal.
/// </summary>
public class FieldValueRulesTests
{
    [Fact]
    public void Null_is_missing() => FieldValueRules.IsMissing(null).Should().BeTrue();

    [Fact]
    public void Empty_string_is_missing() => FieldValueRules.IsMissing("").Should().BeTrue();

    [Fact]
    public void Whitespace_only_string_is_missing() => FieldValueRules.IsMissing("   ").Should().BeTrue();

    [Fact]
    public void Empty_guid_is_missing() => FieldValueRules.IsMissing(Guid.Empty).Should().BeTrue();

    [Fact]
    public void Non_empty_guid_is_not_missing() =>
        FieldValueRules.IsMissing(Guid.NewGuid()).Should().BeFalse();

    [Fact]
    public void Zero_int_is_not_missing() => FieldValueRules.IsMissing(0).Should().BeFalse();

    [Fact]
    public void Non_blank_string_is_not_missing() => FieldValueRules.IsMissing("x").Should().BeFalse();
}
