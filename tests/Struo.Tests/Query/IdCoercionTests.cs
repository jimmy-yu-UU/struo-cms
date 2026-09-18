using AwesomeAssertions;
using Struo.Infrastructure.Query;
using Xunit;

namespace Struo.Tests.Query;

public class IdCoercionTests
{
    [Fact]
    public void Coerce_string_to_guid()
    {
        var g = Guid.NewGuid();
        IdCoercion.Coerce(g.ToString(), typeof(Guid)).Should().Be(g);
    }

    [Fact]
    public void Coerce_guid_to_guid_is_identity()
    {
        var g = Guid.NewGuid();
        IdCoercion.Coerce(g, typeof(Guid)).Should().Be(g);
    }

    [Fact]
    public void Coerce_string_to_nullable_guid()
    {
        var g = Guid.NewGuid();
        IdCoercion.Coerce(g.ToString(), typeof(Guid?)).Should().Be(g);
    }

    [Fact]
    public void Coerce_long_path_unchanged()
    {
        IdCoercion.Coerce("42", typeof(long)).Should().Be(42L);
        IdCoercion.Coerce(7, typeof(int)).Should().Be(7);
    }

    [Fact]
    public void Coerce_null_returns_null() =>
        IdCoercion.Coerce(null, typeof(Guid)).Should().BeNull();

    // Regression guard: a translatable sidecar's nullable Guid FK (e.g. a per-locale OG image)
    // sent as an empty string must coerce to null, not throw FormatException via Guid.Parse("")
    // in the translation-sync path (which would surface as an HTTP 500 on POST
    // /api/items/{collection} — found on live Postgres).
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Coerce_blank_string_to_nullable_guid_is_null(string value) =>
        IdCoercion.Coerce(value, typeof(Guid?)).Should().BeNull();

    [Fact]
    public void Coerce_blank_string_to_guid_is_null() =>
        IdCoercion.Coerce("", typeof(Guid)).Should().BeNull();
}
