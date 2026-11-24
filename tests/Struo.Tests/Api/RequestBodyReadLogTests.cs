using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Struo.Api.Http;
using Xunit;

namespace Struo.Tests.Api;

public class RequestBodyReadLogTests
{
    [Theory]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status408RequestTimeout)]
    public void A_4xx_body_read_failure_is_one_Warning_with_the_exception_and_the_status(int status)
    {
        var logger = new RecordingLogger();
        var failure = new BadHttpRequestException("server detail", status);

        RequestBodyReadLog.Rejected(logger, failure);

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Be($"The request body could not be read (status {status}).");
        entry.Exception.Should().BeSameAs(failure);
    }

    [Fact]
    public void A_413_is_the_size_cap_and_is_not_logged()
    {
        var logger = new RecordingLogger();

        RequestBodyReadLog.Rejected(logger,
            new BadHttpRequestException("too large", StatusCodes.Status413PayloadTooLarge));

        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public void A_logger_with_Warning_disabled_or_no_logger_is_not_written_to()
    {
        var failure = new BadHttpRequestException("server detail", StatusCodes.Status400BadRequest);

        var disabled = () => RequestBodyReadLog.Rejected(NullLogger.Instance, failure);
        var missing = () => RequestBodyReadLog.Rejected(null, failure);

        disabled.Should().NotThrow();
        missing.Should().NotThrow();
    }
}
