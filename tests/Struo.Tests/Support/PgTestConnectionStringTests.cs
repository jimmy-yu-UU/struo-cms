using AwesomeAssertions;
using Xunit;

namespace Struo.Tests.Support;

public sealed class PgTestConnectionStringTests
{
    [Fact]
    public void DisablePooling_AppendsPoolingFalse_WhenTheKeyIsAbsent()
    {
        PgTestConnectionString.DisablePooling("Host=localhost;Database=web-struo-cms-test-db")
            .Should().Be("Host=localhost;Database=web-struo-cms-test-db;Pooling=false");
    }

    [Fact]
    public void DisablePooling_DoesNotDoubleTheSeparator_WhenTheInputEndsWithOne()
    {
        PgTestConnectionString.DisablePooling("Host=localhost;Database=t-test;")
            .Should().Be("Host=localhost;Database=t-test;Pooling=false");
    }

    // An explicit caller choice wins: whoever sets Pooling themselves is opting into pool behaviour
    // knowingly (e.g. to re-investigate the abort documented in AGENTS.md), so don't override it.
    [Theory]
    [InlineData("Pooling=true;Host=localhost")] // the `^` branch: Pooling as the very first key
    [InlineData("Host=localhost;Pooling=true")]
    [InlineData("Host=localhost;pooling=TRUE;Database=t-test")]
    [InlineData("Host=localhost; Pooling = true ;Database=t-test")]
    public void DisablePooling_LeavesAnExplicitPoolingSettingAlone(string connectionString)
    {
        PgTestConnectionString.DisablePooling(connectionString).Should().Be(connectionString);
    }

    // "Pooling" must match as a whole key, not as a substring of some other key's name or value.
    [Fact]
    public void DisablePooling_DoesNotMistakeASimilarlyNamedKeyForPooling()
    {
        PgTestConnectionString.DisablePooling("Host=localhost;Application Name=Pooling-probe")
            .Should().Be("Host=localhost;Application Name=Pooling-probe;Pooling=false");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DisablePooling_LeavesAnEmptyConnectionStringAlone(string connectionString)
    {
        // Empty means "PG not configured" — the suite skips, and there is nothing to shape.
        PgTestConnectionString.DisablePooling(connectionString).Should().Be(connectionString);
    }
}
