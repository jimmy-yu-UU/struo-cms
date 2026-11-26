using System.Text;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Struo.Api.Auth;
using Struo.Api.Http;
using Struo.Application.Files;
using Struo.Domain;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// The request-body handling of the upload endpoint, driven without a server: the body-size cap follows
/// <c>Struo:Files:MaxUploadBytes</c> plus the multipart headroom, a body over that cap is refused as
/// <see cref="PayloadTooLargeException"/> naming the limit, and a form that cannot be read is a 400.
/// </summary>
public class UploadRequestBodyTests
{
    private const long MaxUploadBytes = 104_857_600;

    /// <summary>The ways reading the form fails because of what the client sent.</summary>
    public enum ClientFault { MalformedForm, StreamEnded }

    /// <summary>Failures on the server's side of the read, which are not the caller's mistake.</summary>
    public enum ServerFault { UnixNoSpace, UnixQuota, WindowsDiskFull, WindowsHandleDiskFull }

    private static Exception Fault(ClientFault kind) => kind switch
    {
        ClientFault.MalformedForm => new InvalidDataException("Missing content-type boundary."),
        _ => new IOException("Unexpected end of Stream, the content may have already been read by another component."),
    };

    private static IOException Fault(ServerFault kind) => kind switch
    {
        ServerFault.UnixNoSpace => new IOException("No space left on device", 28),
        ServerFault.UnixQuota => new IOException("Disk quota exceeded", 122),
        ServerFault.WindowsDiskFull => new IOException("There is not enough space on the disk.", unchecked((int)0x80070070)),
        _ => new IOException("The disk is full.", unchecked((int)0x80070027)),
    };

    private static DefaultHttpContext Multipart(Stream body, IServiceProvider? services = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.ContentType = "multipart/form-data; boundary=xyz";
        context.Request.Body = body;
        if (services is not null) context.RequestServices = services;
        return context;
    }

    private static MemoryStream Form(int fieldCount, int filePartBytes = 0)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < fieldCount; i++)
            sb.Append($"--xyz\r\nContent-Disposition: form-data; name=\"f{i}\"\r\n\r\nv\r\n");
        if (filePartBytes > 0)
            sb.Append("--xyz\r\nContent-Disposition: form-data; name=\"file\"; filename=\"a.bin\"\r\n" +
                      "Content-Type: application/octet-stream\r\n\r\n")
                .Append('a', filePartBytes).Append("\r\n");
        sb.Append("--xyz--\r\n");
        return new MemoryStream(Encoding.ASCII.GetBytes(sb.ToString()));
    }

    private static ServiceProvider FormOptionsWith(Action<FormOptions> configure)
    {
        var options = new FormOptions();
        configure(options);
        return new ServiceCollection().AddSingleton(Options.Create(options)).BuildServiceProvider();
    }

    [Fact]
    public void The_body_cap_is_the_upload_limit_plus_the_multipart_headroom()
    {
        var feature = new RecordingLimitFeature();
        var context = Multipart(Stream.Null);
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        UploadRequestBody.RaiseBodyLimit(context.Request, MaxUploadBytes, NullLogger.Instance);

        feature.MaxRequestBodySize.Should().Be(MaxUploadBytes + FileStorageOptions.UploadFramingHeadroomBytes);
    }

    [Fact]
    public void The_cap_follows_a_limit_below_the_server_default_too()
    {
        var feature = new RecordingLimitFeature();
        var context = Multipart(Stream.Null);
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        UploadRequestBody.RaiseBodyLimit(context.Request, 64, NullLogger.Instance);

        feature.MaxRequestBodySize.Should().Be(64 + FileStorageOptions.UploadFramingHeadroomBytes);
    }

    [Fact]
    public void A_read_only_limit_feature_is_left_alone_and_one_warning_is_logged_for_the_request()
    {
        var feature = new RecordingLimitFeature(isReadOnly: true);
        var context = Multipart(Stream.Null);
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);
        var logger = new RecordingLogger();

        UploadRequestBody.RaiseBodyLimit(context.Request, MaxUploadBytes, logger);

        feature.MaxRequestBodySize.Should().Be(30_000_000);
        var warning = logger.Entries.Should().ContainSingle().Which;
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Message.Should().Contain("Struo:Files:MaxUploadBytes");
    }

    [Fact]
    public void A_host_without_a_limit_feature_is_not_an_error()
    {
        var context = Multipart(Stream.Null);
        var logger = new RecordingLogger();

        var act = () => UploadRequestBody.RaiseBodyLimit(context.Request, MaxUploadBytes, logger);

        act.Should().NotThrow();
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task A_well_formed_body_is_read_as_a_form()
    {
        var context = Multipart(Form(fieldCount: 1));

        var form = await UploadRequestBody.ReadFormAsync(
            context.Request, MaxUploadBytes, NullLogger.Instance, CancellationToken.None);

        form["f0"].ToString().Should().Be("v");
    }

    [Fact]
    public async Task The_multipart_limit_is_raised_from_the_configured_form_options_not_from_defaults()
    {
        using var services = FormOptionsWith(o => o.MultipartBodyLengthLimit = 1024);
        var context = Multipart(Form(fieldCount: 0, filePartBytes: 3000), services);

        var form = await UploadRequestBody.ReadFormAsync(context.Request, 4096, NullLogger.Instance, CancellationToken.None);

        form.Files.GetFile("file")!.Length.Should().Be(3000);
    }

    [Fact]
    public async Task The_other_configured_form_limits_survive_the_raise()
    {
        using var services = FormOptionsWith(o =>
        {
            o.MultipartBodyLengthLimit = 1024;
            o.ValueCountLimit = 2;
        });
        var context = Multipart(Form(fieldCount: 3), services);

        var act = () => UploadRequestBody.ReadFormAsync(context.Request, 4096, NullLogger.Instance, CancellationToken.None);

        (await act.Should().ThrowAsync<QueryException>()).Which.Message.Should().Be(UploadRequestBody.UnreadableFormMessage);
    }

    [Fact]
    public void A_configured_multipart_limit_above_the_cap_is_left_as_it_is()
    {
        using var services = FormOptionsWith(_ => { });
        var context = Multipart(Stream.Null, services);

        UploadRequestBody.RaiseBodyLimit(context.Request, 4096, NullLogger.Instance);

        context.Features.Get<IFormFeature>().Should().BeNull("the configured limit already allows the upload");
    }

    [Fact]
    public async Task A_body_over_the_cap_is_PayloadTooLarge_naming_the_upload_limit()
    {
        var kestrel413 = new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge);
        var context = Multipart(new FailingBodyStream(kestrel413));

        var act = () => UploadRequestBody.ReadFormAsync(
            context.Request, MaxUploadBytes, NullLogger.Instance, CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<PayloadTooLargeException>()).Which;
        thrown.Message.Should().Be($"File exceeds the maximum size of {MaxUploadBytes} bytes.");
        thrown.Field.Should().Be("file");
        thrown.MaxBytes.Should().Be(MaxUploadBytes);
    }

    [Theory]
    [InlineData(ClientFault.MalformedForm)]
    [InlineData(ClientFault.StreamEnded)]
    public async Task A_form_that_cannot_be_read_is_a_bad_input_error_with_a_fixed_message(ClientFault kind)
    {
        var failure = Fault(kind);
        var context = Multipart(new FailingBodyStream(failure));
        var logger = new RecordingLogger();

        var act = () => UploadRequestBody.ReadFormAsync(context.Request, MaxUploadBytes, logger, CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<QueryException>()).Which;
        thrown.Message.Should().Be("The request form could not be read.");
        thrown.Message.Should().NotContain(failure.Message);
    }

    [Theory]
    [InlineData(ClientFault.MalformedForm)]
    [InlineData(ClientFault.StreamEnded)]
    public async Task A_form_that_cannot_be_read_is_logged_once_at_warning_with_the_exception_attached(ClientFault kind)
    {
        var failure = Fault(kind);
        var context = Multipart(new FailingBodyStream(failure));
        var logger = new RecordingLogger();

        var act = () => UploadRequestBody.ReadFormAsync(context.Request, MaxUploadBytes, logger, CancellationToken.None);
        await act.Should().ThrowAsync<QueryException>();

        var entry = logger.Entries.Should().ContainSingle().Which;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Exception.Should().BeSameAs(failure);
        entry.Message.Should().NotContain("xyz", "no request content is logged");
    }

    [Theory]
    [InlineData(ServerFault.UnixNoSpace)]
    [InlineData(ServerFault.UnixQuota)]
    [InlineData(ServerFault.WindowsDiskFull)]
    [InlineData(ServerFault.WindowsHandleDiskFull)]
    public async Task A_disk_fault_while_buffering_the_form_is_left_to_surface_as_a_server_error(ServerFault kind)
    {
        var failure = Fault(kind);
        var context = Multipart(new FailingBodyStream(failure));
        var logger = new RecordingLogger();

        var act = () => UploadRequestBody.ReadFormAsync(context.Request, MaxUploadBytes, logger, CancellationToken.None);

        (await act.Should().ThrowAsync<IOException>()).Which.Should().BeSameAs(failure);
        logger.Entries.Should().BeEmpty("the unhandled-exception handler logs a server error");
    }

    [Fact]
    public async Task A_read_the_server_timed_out_is_a_408_with_a_fixed_message_and_a_warning()
    {
        var timeout = new BadHttpRequestException("Request body timeout.", StatusCodes.Status408RequestTimeout);
        var context = Multipart(new FailingBodyStream(timeout));
        var logger = new RecordingLogger();

        var act = () => UploadRequestBody.ReadFormAsync(context.Request, MaxUploadBytes, logger, CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<StruoDomainException>()).Which;
        thrown.Code.Should().Be("BAD_USER_INPUT");
        thrown.Status.Should().Be(StatusCodes.Status408RequestTimeout);
        thrown.Message.Should().Be("The request body could not be read.");
        logger.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public async Task A_timeout_wrapped_by_the_form_reader_is_a_408_too()
    {
        var timeout = new BadHttpRequestException("Request body timeout.", StatusCodes.Status408RequestTimeout);
        var context = Multipart(new FailingBodyStream(new InvalidDataException("form", timeout)));

        var act = () => UploadRequestBody.ReadFormAsync(
            context.Request, MaxUploadBytes, NullLogger.Instance, CancellationToken.None);

        (await act.Should().ThrowAsync<StruoDomainException>()).Which.Status.Should().Be(StatusCodes.Status408RequestTimeout);
    }

    [Fact]
    public async Task Another_request_the_server_rejected_is_passed_on_for_the_shared_error_map_to_answer_as_a_client_error()
    {
        var rejected = new BadHttpRequestException("Bad chunk.", StatusCodes.Status400BadRequest);
        var context = Multipart(new FailingBodyStream(rejected));
        var logger = new RecordingLogger();

        var act = () => UploadRequestBody.ReadFormAsync(context.Request, MaxUploadBytes, logger, CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<BadHttpRequestException>()).Which;
        thrown.Should().BeSameAs(rejected);
        var (status, body) = StruoExceptionHandler.Map(thrown, authenticated: true);
        status.Should().Be(StatusCodes.Status400BadRequest);
        body.Code.Should().Be("BAD_USER_INPUT");
        body.Message.Should().Be("The request body could not be read.");
        logger.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ClientFault.MalformedForm)]
    [InlineData(ClientFault.StreamEnded)]
    public async Task A_form_read_that_fails_after_the_client_left_is_a_cancellation_not_a_bad_request(ClientFault kind)
    {
        var context = Multipart(new FailingBodyStream(Fault(kind)));
        using var aborted = new CancellationTokenSource();
        context.RequestAborted = aborted.Token;
        await aborted.CancelAsync();
        var logger = new RecordingLogger();

        var act = () => UploadRequestBody.ReadFormAsync(context.Request, MaxUploadBytes, logger, CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        logger.Entries.Should().BeEmpty("a client that left is not worth a warning");
    }

    [Fact]
    public async Task A_request_the_server_rejected_after_the_client_left_is_a_cancellation_too()
    {
        var early = new BadHttpRequestException("Unexpected end of request content.", StatusCodes.Status400BadRequest);
        var context = Multipart(new FailingBodyStream(early));
        using var aborted = new CancellationTokenSource();
        context.RequestAborted = aborted.Token;
        await aborted.CancelAsync();

        var act = () => UploadRequestBody.ReadFormAsync(
            context.Request, MaxUploadBytes, NullLogger.Instance, CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task An_unrelated_failure_is_left_alone()
    {
        var failure = new InvalidOperationException("boom");
        var context = Multipart(new FailingBodyStream(failure));

        var act = () => UploadRequestBody.ReadFormAsync(
            context.Request, MaxUploadBytes, NullLogger.Instance, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
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
/// multipart form: the caller's write grant is checked first, then the cap is set and an over-cap body
/// refused there.
/// </summary>
public class UploadBodyLimitFilterTests
{
    private const long MaxUploadBytes = 1024;

    private sealed class Policy(bool canWrite) : IFileAccessPolicy
    {
        public bool CanWrite() => canWrite;
        public bool CanDelete() => canWrite;
        public bool CanReadFolders() => canWrite;
        public bool CanReadUnpublished() => canWrite;
    }

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

    private static UploadBodyLimitFilter Filter(bool canWrite = true) =>
        new(new FileStorageOptions { MaxUploadBytes = MaxUploadBytes }, new Policy(canWrite),
            NullLogger<UploadBodyLimitFilter>.Instance);

    private static Task<ResourceExecutedContext> Executed(ResourceExecutingContext context) =>
        Task.FromResult(new ResourceExecutedContext(context, []));

    [Fact]
    public async Task A_multipart_request_gets_the_upload_cap_and_its_form_is_read_before_the_action()
    {
        const string body = "--xyz\r\nContent-Disposition: form-data; name=\"a\"\r\n\r\nb\r\n--xyz--\r\n";
        var (context, http) = Context("multipart/form-data; boundary=xyz", new MemoryStream(Encoding.ASCII.GetBytes(body)));
        var feature = new RecordingLimitFeature();
        http.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);
        var nextCalled = false;

        await Filter().OnResourceExecutionAsync(context, () => { nextCalled = true; return Executed(context); });

        nextCalled.Should().BeTrue();
        feature.MaxRequestBodySize.Should().Be(MaxUploadBytes + FileStorageOptions.UploadFramingHeadroomBytes);
        (await http.Request.ReadFormAsync())["a"].ToString().Should().Be("b");
    }

    [Fact]
    public async Task A_request_that_is_not_a_form_is_passed_on_untouched()
    {
        var (context, http) = Context("application/json", Stream.Null);
        var feature = new RecordingLimitFeature();
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
        var (context, _) = Context("multipart/form-data; boundary=xyz", new FailingBodyStream(kestrel413));
        var nextCalled = false;

        var act = () => Filter().OnResourceExecutionAsync(context, () => { nextCalled = true; return Executed(context); });

        (await act.Should().ThrowAsync<PayloadTooLargeException>()).Which.MaxBytes.Should().Be(MaxUploadBytes);
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task A_caller_without_the_write_grant_is_refused_before_the_body_is_touched()
    {
        var body = new CountingBodyStream(new MemoryStream(new byte[4096]));
        var (context, http) = Context("multipart/form-data; boundary=xyz", body);
        var feature = new RecordingLimitFeature();
        http.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);
        var nextCalled = false;

        var act = () => Filter(canWrite: false)
            .OnResourceExecutionAsync(context, () => { nextCalled = true; return Executed(context); });

        (await act.Should().ThrowAsync<PermissionDeniedException>()).Which.Message.Should().Be("Write not permitted.");
        body.Bytes.Should().Be(0);
        feature.MaxRequestBodySize.Should().Be(30_000_000);
        nextCalled.Should().BeFalse();
    }
}
