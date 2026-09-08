// tests/Struo.Tests/Api/ProgramCultureTests.cs
using System.Globalization;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// Proves the process-wide invariant-culture fix in Program.cs actually runs as part of real host
/// startup, not just something the direct translator+SqlSugar pairing in
/// ConditionalModelTranslatorTests exercises on its own. WebApplicationFactory&lt;Program&gt; invokes
/// the real top-level statements in Program.cs (everything up to, but not including, app.Run()) the
/// first time the host is built, so forcing that via CreateClient() is enough to trigger it.
/// Asserting CultureInfo.DefaultThreadCurrentCulture/UICulture (rather than CultureInfo.CurrentCulture
/// inside a request) is the provable, non-overkill check here: it is the exact static Program.cs sets,
/// its effect (every new thread defaults to it) is what actually protects SqlSugar's re-parse of
/// ConditionalModel.FieldValue, and — unlike issuing a REST filter query — it needs no decimal-typed
/// field on a real seeded collection (none of the shipped core/sample collections has one).
/// </summary>
[Collection("ApiIntegration")]
public class ProgramCultureTests(ApiFactory factory)
{
    [Fact]
    public void Host_startup_sets_the_invariant_culture_as_the_process_default()
    {
        using var client = factory.CreateClient(); // forces the host to actually build, if it hasn't yet

        CultureInfo.DefaultThreadCurrentCulture.Should().NotBeNull();
        CultureInfo.DefaultThreadCurrentCulture!.Name.Should().Be(string.Empty, "the invariant culture's Name is the empty string");
        CultureInfo.DefaultThreadCurrentUICulture.Should().NotBeNull();
        CultureInfo.DefaultThreadCurrentUICulture!.Name.Should().Be(string.Empty);
    }
}
