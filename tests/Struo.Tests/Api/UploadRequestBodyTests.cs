using System.Text;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using Struo.Api.Http;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// The request-body handling of the upload endpoint, driven without a server: the body-size cap follows
/// <c>Struo:Files:MaxUploadBytes</c> plus the multipart headroom, and a body over that cap is refused as
/// <see cref="PayloadTooLargeException"/> naming the limit.
/// </summary>
public class UploadRequestBodyTests
{
    private const long MaxUploadBytes = 104_857_600;

    private sealed class FakeLimitFeature(bool isReadOnly = false) : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly { get; } = isReadOnly;
        public long? MaxRequestBodySize { get; set; } = 30_000_000;
    }

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

    private static DefaultHttpContext Multipart(Stream body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.ContentType = "multipart/form-data; boundary=xyz";
        context.Request.Body = body;
        return context;
    }

    [Fact]
    public void The_body_cap_is_the_upload_limit_plus_the_multipart_headroom()
    {
        var feature = new FakeLimitFeature();
        var context = Multipart(Stream.Null);
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        UploadRequestBody.RaiseBodyLimit(context.Request, MaxUploadBytes);

        feature.MaxRequestBodySize.Should().Be(MaxUploadBytes + UploadRequestBody.MultipartHeadroomBytes);
    }

    [Fact]
    public void The_cap_follows_a_limit_below_the_server_default_too()
    {
        var feature = new FakeLimitFeature();
        var context = Multipart(Stream.Null);
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        UploadRequestBody.RaiseBodyLimit(context.Request, 64);

        feature.MaxRequestBodySize.Should().Be(64 + UploadRequestBody.MultipartHeadroomBytes);
    }

    [Fact]
    public void A_read_only_limit_feature_is_left_alone()
    {
        var feature = new FakeLimitFeature(isReadOnly: true);
        var context = Multipart(Stream.Null);
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        UploadRequestBody.RaiseBodyLimit(context.Request, MaxUploadBytes);

        feature.MaxRequestBodySize.Should().Be(30_000_000);
    }

    [Fact]
    public void A_host_without_a_limit_feature_is_not_an_error()
    {
        var context = Multipart(Stream.Null);

        var act = () => UploadRequestBody.RaiseBodyLimit(context.Request, MaxUploadBytes);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task A_well_formed_body_is_read_as_a_form()
    {
        const string body =
            "--xyz\r\nContent-Disposition: form-data; name=\"folderId\"\r\n\r\nabc\r\n--xyz--\r\n";
        var context = Multipart(new MemoryStream(Encoding.ASCII.GetBytes(body)));

        var form = await UploadRequestBody.ReadFormAsync(context.Request, MaxUploadBytes, CancellationToken.None);

        form["folderId"].ToString().Should().Be("abc");
    }

    [Fact]
    public async Task A_body_over_the_cap_is_PayloadTooLarge_naming_the_upload_limit()
    {
        var kestrel413 = new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge);
        var context = Multipart(new FailingStream(kestrel413));

        var act = () => UploadRequestBody.ReadFormAsync(context.Request, MaxUploadBytes, CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<PayloadTooLargeException>()).Which;
        thrown.Message.Should().Be($"File exceeds the maximum size of {MaxUploadBytes} bytes.");
        thrown.Field.Should().Be("file");
        thrown.MaxBytes.Should().Be(MaxUploadBytes);
    }

    [Fact]
    public async Task Another_unreadable_body_is_not_reported_as_too_large()
    {
        var other = new BadHttpRequestException("Unexpected end of request content.", StatusCodes.Status400BadRequest);
        var context = Multipart(new FailingStream(other));

        var act = () => UploadRequestBody.ReadFormAsync(context.Request, MaxUploadBytes, CancellationToken.None);

        await act.Should().NotThrowAsync<PayloadTooLargeException>();
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public void The_file_too_large_error_is_one_shared_shape()
    {
        var error = PayloadTooLargeException.FileTooLarge(25);

        error.Message.Should().Be("File exceeds the maximum size of 25 bytes.");
        error.Field.Should().Be("file");
        error.MaxBytes.Should().Be(25);
    }
}

/// <summary>
/// <see cref="UploadBodyLimitFilter"/> runs before MVC binds the action, which is when MVC reads the
/// multipart form: the cap must be set and an over-cap body refused there.
/// </summary>
public class UploadBodyLimitFilterTests
{
    private const long MaxUploadBytes = 1024;

    private static (ResourceExecutingContext Context, DefaultHttpContext Http) Context(string? contentType, Stream body)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.ContentType = contentType;
        http.Request.Body = body;
        var actionContext = new Microsoft.AspNetCore.Mvc.ActionContext(
            http, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        return (new ResourceExecutingContext(actionContext, [], []), http);
    }

    private static UploadBodyLimitFilter Filter() =>
        new(new Struo.Application.Files.FileStorageOptions { MaxUploadBytes = MaxUploadBytes });

    private static Task<ResourceExecutedContext> Executed(ResourceExecutingContext context) =>
        Task.FromResult(new ResourceExecutedContext(context, []));

    private sealed class LimitFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;
        public long? MaxRequestBodySize { get; set; } = 30_000_000;
    }

    [Fact]
    public async Task A_multipart_request_gets_the_upload_cap_and_its_form_is_read_before_the_action()
    {
        const string body = "--xyz\r\nContent-Disposition: form-data; name=\"a\"\r\n\r\nb\r\n--xyz--\r\n";
        var (context, http) = Context("multipart/form-data; boundary=xyz", new MemoryStream(Encoding.ASCII.GetBytes(body)));
        var feature = new LimitFeature();
        http.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);
        var nextCalled = false;

        await Filter().OnResourceExecutionAsync(context, () => { nextCalled = true; return Executed(context); });

        nextCalled.Should().BeTrue();
        feature.MaxRequestBodySize.Should().Be(MaxUploadBytes + UploadRequestBody.MultipartHeadroomBytes);
        http.Request.HasFormContentType.Should().BeTrue();
        (await http.Request.ReadFormAsync())["a"].ToString().Should().Be("b");
    }

    [Fact]
    public async Task A_request_that_is_not_a_form_is_passed_on_untouched()
    {
        var (context, http) = Context("application/json", Stream.Null);
        var feature = new LimitFeature();
        http.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);
        var nextCalled = false;

        await Filter().OnResourceExecutionAsync(context, () => { nextCalled = true; return Executed(context); });

        nextCalled.Should().BeTrue();
        feature.MaxRequestBodySize.Should().Be(30_000_000);
    }

    [Fact]
    public async Task A_form_over_the_cap_is_refused_before_the_action_runs()
    {
        var kestrel413 = new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge);
        var (context, _) = Context("multipart/form-data; boundary=xyz", new ThrowingBody(kestrel413));
        var nextCalled = false;

        var act = () => Filter().OnResourceExecutionAsync(context, () => { nextCalled = true; return Executed(context); });

        var thrown = (await act.Should().ThrowAsync<PayloadTooLargeException>()).Which;
        thrown.MaxBytes.Should().Be(MaxUploadBytes);
        nextCalled.Should().BeFalse();
    }

    private sealed class ThrowingBody(Exception failure) : Stream
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
}
