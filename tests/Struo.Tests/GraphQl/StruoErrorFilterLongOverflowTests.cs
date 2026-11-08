// tests/Struo.Tests/GraphQl/StruoErrorFilterLongOverflowTests.cs
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Language;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Struo.Api.GraphQl;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>
/// Only a Long input-coercion failure that HotChocolate itself raised becomes a client error; a
/// FormatException from anywhere else stays an unhandled, masked, Error-logged failure.
/// </summary>
public class StruoErrorFilterLongOverflowTests
{
    private sealed class CapturingLogger : ILogger<StruoErrorFilter>
    {
        public List<LogLevel> Levels { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }

    private static (IError Mapped, CapturingLogger Log) Run(Exception exception, params (string Key, object Value)[] extensions)
    {
        var log = new CapturingLogger();
        var filter = new StruoErrorFilter(log, new HttpContextAccessor { HttpContext = null });
        var builder = ErrorBuilder.New().SetMessage("original").SetException(exception);
        foreach (var (key, value) in extensions) builder.SetExtension(key, value);
        return (filter.OnError(builder.Build()), log);
    }

    [Fact]
    public void A_FormatException_thrown_by_a_resolver_is_masked_as_internal_and_logged_as_an_error()
    {
        var (mapped, log) = Run(new FormatException("boom"));

        mapped.Code.Should().Be("INTERNAL_SERVER_ERROR");
        mapped.Message.Should().Be("An internal error occurred.");
        log.Levels.Should().Contain(LogLevel.Error);
    }

    [Fact]
    public void A_FormatException_on_a_non_Long_input_stays_internal()
    {
        var (mapped, _) = Run(new FormatException("x"), ("fieldType", "Int"), ("inputPath", "version"));

        mapped.Code.Should().Be("INTERNAL_SERVER_ERROR");
    }

    [Fact]
    public void A_Long_literal_overflow_on_version_is_INVALID_VERSION_without_an_error_log()
    {
        var (mapped, log) = Run(new InvalidFormatException("The value is not a valid long."),
            ("fieldType", "Long"), ("inputPath", "input.version"), ("coordinate", "FxArticleUpdateInput.version"));

        mapped.Code.Should().Be("INVALID_VERSION");
        mapped.Message.Should().NotContain("valid long");
        mapped.Exception.Should().BeNull();
        log.Levels.Should().NotContain(LogLevel.Error);
    }

    [Fact]
    public void A_Long_literal_overflow_on_another_field_is_BAD_USER_INPUT()
    {
        var (mapped, log) = Run(new InvalidFormatException("x"),
            ("fieldType", "Long"), ("inputPath", "input.size"), ("coordinate", "FxArticleUpdateInput.size"));

        mapped.Code.Should().Be("BAD_USER_INPUT");
        log.Levels.Should().NotContain(LogLevel.Error);
    }

    [Fact]
    public void A_Long_variable_overflow_is_BAD_USER_INPUT_even_when_the_variable_is_named_version()
    {
        var (mapped, log) = Run(new FormatException("x"), ("fieldType", "Long"), ("inputPath", "version"));

        mapped.Code.Should().Be("BAD_USER_INPUT");
        log.Levels.Should().NotContain(LogLevel.Error);
    }
}
