using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Struo.Api.Http;
using Struo.Domain.Query;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>REST handler side of SEARCH_UNAVAILABLE: 503, fixed message, and a Warning (not Error) log carrying the real exception.</summary>
public class SearchUnavailableMappingTests
{
    [Fact]
    public void Handler_returns_503_fixed_message_and_logs_a_warning()
    {
        var logger = new ListLogger<StruoExceptionHandler>();
        var (status, body) = StruoExceptionHandler.Map(new SearchUnavailableException("meili down"), authenticated: true, logger);
        status.Should().Be(503);
        body.Code.Should().Be("SEARCH_UNAVAILABLE");
        body.Message.Should().Be("Search is temporarily unavailable.");
        logger.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public void Unmapped_exceptions_still_log_at_error()
    {
        var logger = new ListLogger<StruoExceptionHandler>();
        StruoExceptionHandler.Map(new InvalidOperationException("boom"), authenticated: true, logger);
        logger.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Error);
    }
}
