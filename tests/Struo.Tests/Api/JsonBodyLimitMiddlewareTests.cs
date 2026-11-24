using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Struo.Tests.Support;
using Struo.Api.Http;
using Struo.Application.Configuration;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// The GraphQL buffering path of <see cref="JsonBodyLimitMiddleware"/>, driven without a server: a body
/// that fails to read for a reason other than the cap is answered as a GraphQL error carrying that
/// failure's status, and a request that cannot have a body is passed on untouched.
/// </summary>
public class JsonBodyLimitMiddlewareTests
{
    private sealed class FailingStream(Exception failure) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw failure;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => throw failure;
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class NoBodyFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => false;
    }

    private static JsonBodyLimitMiddleware Middleware(RequestDelegate next, ILogger<JsonBodyLimitMiddleware>? logger = null) =>
        new(next, Options.Create(new ApiOptions { MaxJsonBodyBytes = 1024 }), logger ?? NullLogger<JsonBodyLimitMiddleware>.Instance);

    private static DefaultHttpContext GraphQlContext(string method, Stream body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = "/graphql";
        context.Request.ContentType = "application/json";
        context.Request.Body = body;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static JsonElement ErrorOf(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        return JsonDocument.Parse(context.Response.Body).RootElement.GetProperty("errors")[0];
    }

    [Theory]
    [InlineData(StatusCodes.Status408RequestTimeout)]
    [InlineData(StatusCodes.Status400BadRequest)]
    public async Task A_graphql_body_that_fails_to_read_is_a_graphql_error_with_that_status(int status)
    {
        var called = false;
        var context = GraphQlContext("POST", new FailingStream(new BadHttpRequestException("read failed", status)));

        await Middleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        called.Should().BeFalse();
        context.Response.StatusCode.Should().Be(status);
        var error = ErrorOf(context);
        error.GetProperty("extensions").GetProperty("code").GetString().Should().NotBe("INTERNAL_SERVER_ERROR");
        error.GetProperty("message").GetString().Should().NotContain("read failed");
    }

    [Theory]
    [InlineData(StatusCodes.Status500InternalServerError)]
    [InlineData(StatusCodes.Status503ServiceUnavailable)]
    public async Task A_graphql_body_read_that_fails_without_a_client_error_status_propagates(int status)
    {
        var called = false;
        var failure = new BadHttpRequestException("read failed", status);
        var context = GraphQlContext("POST", new FailingStream(failure));

        var thrown = await Assert.ThrowsAsync<BadHttpRequestException>(() =>
            Middleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context));

        thrown.Should().BeSameAs(failure);
        called.Should().BeFalse();
    }

    [Fact]
    public async Task A_graphql_body_over_the_cap_from_kestrel_is_still_413_PAYLOAD_TOO_LARGE()
    {
        var context = GraphQlContext("POST",
            new FailingStream(new BadHttpRequestException("too large", StatusCodes.Status413PayloadTooLarge)));

        await Middleware(_ => Task.CompletedTask).InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
        var error = ErrorOf(context);
        error.GetProperty("extensions").GetProperty("code").GetString().Should().Be("PAYLOAD_TOO_LARGE");
        error.GetProperty("message").GetString().Should().Be(JsonBodyLimitMiddleware.TooLargeMessage);
    }

    [Fact]
    public async Task A_request_that_cannot_have_a_body_is_passed_on_without_reading_it()
    {
        var original = new FailingStream(new InvalidOperationException("must not be read"));
        var context = GraphQlContext("GET", original);
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new NoBodyFeature());
        Stream? seen = null;

        await Middleware(c => { seen = c.Request.Body; return Task.CompletedTask; }).InvokeAsync(context);

        seen.Should().BeSameAs(original);
    }

    [Theory]
    [InlineData(StatusCodes.Status408RequestTimeout)]
    [InlineData(StatusCodes.Status400BadRequest)]
    public async Task A_graphql_body_that_fails_to_read_writes_exactly_one_Warning_with_the_status(int status)
    {
        var logger = new ListLogger<JsonBodyLimitMiddleware>();
        var context = GraphQlContext("POST", new FailingStream(new BadHttpRequestException("read failed", status)));

        await Middleware(_ => Task.CompletedTask, logger).InvokeAsync(context);

        logger.Entries.Should().ContainSingle().Which.Should()
            .Be((LogLevel.Warning, $"The request body could not be read (status {status})."));
    }

    [Fact]
    public async Task A_graphql_body_over_the_cap_from_kestrel_writes_no_log_entry()
    {
        var logger = new ListLogger<JsonBodyLimitMiddleware>();
        var context = GraphQlContext("POST",
            new FailingStream(new BadHttpRequestException("too large", StatusCodes.Status413PayloadTooLarge)));

        await Middleware(_ => Task.CompletedTask, logger).InvokeAsync(context);

        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task A_graphql_multipart_request_is_refused_with_415_without_a_log_entry_or_a_body_read()
    {
        var logger = new ListLogger<JsonBodyLimitMiddleware>();
        var called = false;
        var context = GraphQlContext("POST", new FailingStream(new InvalidOperationException("must not be read")));
        context.Request.ContentType = "multipart/form-data; boundary=x";

        await Middleware(_ => { called = true; return Task.CompletedTask; }, logger).InvokeAsync(context);

        called.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status415UnsupportedMediaType);
        ErrorOf(context).GetProperty("extensions").GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task A_rest_multipart_request_is_passed_on()
    {
        var called = false;
        var context = GraphQlContext("POST", new MemoryStream());
        context.Request.Path = "/api/files";
        context.Request.ContentType = "multipart/form-data; boundary=x";

        await Middleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        called.Should().BeTrue();
    }
}
