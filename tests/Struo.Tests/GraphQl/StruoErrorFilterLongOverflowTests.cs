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
/// Only an Int, Long or Float input-coercion failure that HotChocolate itself raised becomes a client
/// error; a FormatException from anywhere else stays an unhandled, masked, Error-logged failure.
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

    private static (IError Mapped, CapturingLogger Log) Run(Exception? exception, params (string Key, object Value)[] extensions)
    {
        var log = new CapturingLogger();
        var filter = new StruoErrorFilter(log, new HttpContextAccessor { HttpContext = null });
        var builder = ErrorBuilder.New().SetMessage("original");
        if (exception is not null) builder.SetException(exception);
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
    public void A_FormatException_on_a_non_numeric_input_stays_internal()
    {
        var (mapped, log) = Run(new FormatException("x"), ("fieldType", "String"), ("inputPath", "version"));

        mapped.Code.Should().Be("INTERNAL_SERVER_ERROR");
        log.Levels.Should().Contain(LogLevel.Error);
    }

    [Theory]
    [InlineData("Int", "input.offset", "Mutation.f(offset:)")]
    [InlineData("Int", "revisionNumber", "Mutation.revertFxArticle(revisionNumber:)")]
    [InlineData("Float", "filter.price.gt", "FloatFilter.gt")]
    [InlineData("Long", "input.size", "FxArticleUpdateInput.size")]
    public void A_literal_overflow_on_a_number_is_BAD_USER_INPUT_without_an_error_log(
        string scalar, string inputPath, string coordinate)
    {
        var (mapped, log) = Run(new InvalidFormatException("x"),
            ("fieldType", scalar), ("inputPath", inputPath), ("coordinate", coordinate));

        mapped.Code.Should().Be("BAD_USER_INPUT");
        mapped.Exception.Should().BeNull();
        log.Levels.Should().NotContain(LogLevel.Error);
    }

    [Theory]
    [InlineData("Int")]
    [InlineData("Float")]
    public void A_variable_overflow_on_a_number_is_BAD_USER_INPUT_without_an_error_log(string scalar)
    {
        var (mapped, log) = Run(new FormatException("x"), ("fieldType", scalar), ("inputPath", "l"));

        mapped.Code.Should().Be("BAD_USER_INPUT");
        mapped.Exception.Should().BeNull();
        log.Levels.Should().NotContain(LogLevel.Error);
    }

    [Theory]
    [InlineData("Int", "must be an integer")]
    [InlineData("Long", "must be an integer")]
    [InlineData("Float", "must be a number")]
    public void The_message_follows_the_scalar_and_carries_no_framework_text(string scalar, string expected)
    {
        var (mapped, _) = Run(new FormatException("x"), ("fieldType", scalar), ("inputPath", "v"));

        mapped.Message.Should().Contain(expected).And.NotContain("coerce").And.NotContain("original");
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

    [Theory]
    [InlineData("Long", "version", "INVALID_VERSION")]
    [InlineData("Long", "size", "BAD_USER_INPUT")]
    [InlineData("Int", "limit", "BAD_USER_INPUT")]
    [InlineData("Int", "revisionNumber", "BAD_USER_INPUT")]
    [InlineData("Float", "price", "BAD_USER_INPUT")]
    public void A_field_literal_of_the_wrong_kind_is_coded_by_its_field_name(string scalar, string field, string code)
    {
        var (mapped, log) = Run(null, ("fieldName", field), ("fieldType", scalar), ("locationType", scalar),
            ("specifiedBy", "https://spec.graphql.org/September2025/#sec-Values-of-Correct-Type"));

        mapped.Code.Should().Be(code);
        mapped.Message.Should().NotBe("original");
        log.Levels.Should().BeEmpty();
    }

    [Theory]
    [InlineData("version", "INVALID_VERSION")]
    [InlineData("revisionNumber", "BAD_USER_INPUT")]
    [InlineData("limit", "BAD_USER_INPUT")]
    public void An_argument_literal_of_the_wrong_kind_is_coded_by_its_argument_name(string argument, string code)
    {
        var (mapped, log) = Run(null, ("argument", argument), ("locationType", "Int!"),
            ("specifiedBy", "https://spec.graphql.org/September2025/#sec-Values-of-Correct-Type"));

        mapped.Code.Should().Be(code);
        log.Levels.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Long")]
    [InlineData("Int")]
    [InlineData("Float")]
    public void A_variable_of_the_wrong_kind_is_BAD_USER_INPUT_even_when_named_version(string scalar)
    {
        var (mapped, log) = Run(null, ("fieldType", scalar), ("inputPath", "version"));

        mapped.Code.Should().Be("BAD_USER_INPUT");
        log.Levels.Should().BeEmpty();
    }

    [Fact]
    public void A_validation_error_with_no_number_scalar_keeps_no_code()
    {
        var (mapped, _) = Run(null, ("fieldName", "status"), ("fieldType", "String"), ("locationType", "String"),
            ("specifiedBy", "https://spec.graphql.org/September2025/#sec-Values-of-Correct-Type"));

        mapped.Code.Should().BeNull();
        mapped.Message.Should().Be("original");
    }

    [Fact]
    public void A_number_scalar_error_without_a_coercion_marker_keeps_no_code()
    {
        var (mapped, _) = Run(null, ("fieldType", "Int"));

        mapped.Code.Should().BeNull();
    }
}
