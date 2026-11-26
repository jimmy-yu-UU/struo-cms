using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
using Struo.Tests.Api;
using Struo.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Struo.Tests.GraphQl;

/// <summary>
/// Requests to <c>/graphql</c> whose query string, multipart body or body read is bad: a client error is
/// answered the same way under every <c>Accept</c> value and is not logged as an error; a body read that
/// fails in another way is a 500 in the GraphQL shape with one error-level entry that carries the exception.
/// </summary>
[Collection("ApiIntegration")]
public class GraphQlRequestShapeTests(ApiFactory factory, ITestOutputHelper output)
{
    private const string Unreadable = "The request body could not be read.";
    private const string ReadWarningTemplate = "The request body could not be read (status {StatusCode}).";
    private const string GraphQlPath = "/graphql";
    private const string Query = "{ __typename }";
    private const string MixedBody = "--xyz\r\nContent-Type: application/json\r\n\r\n{}\r\n--xyz--\r\n";
    private const string MultipartRefused =
        "GraphQL does not accept multipart requests; upload a file through POST /api/files.";
    private const string OperationsJson = "{\"query\":\"{ __typename }\"}";

    private static readonly string?[] AcceptValues =
        [null, "*/*", "application/json", "application/graphql-response+json"];

    private readonly CapturingLogSink _sink = new();
    private readonly List<LogEvent> _requestEvents = [];

    private WebApplicationFactory<Program> Host(Exception? bodyFailure = null, bool clientGone = false) =>
        factory.WithWebHostBuilder(b =>
            b.ConfigureServices(s =>
            {
                s.AddSingleton<ILogEventSink>(_sink);
                if (bodyFailure is not null)
                    s.AddSingleton<IStartupFilter>(new FailingBodyPathFilter(GraphQlPath, bodyFailure, clientGone));
            }));

    private static void Accept(HttpRequestMessage request, string? accept)
    {
        if (accept is not null) request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(accept));
    }

    private async Task<(HttpStatusCode Status, string Body)> SendAsync(
        WebApplicationFactory<Program> host, HttpRequestMessage request, string variant)
    {
        var client = host.CreateClient();
        var before = _sink.Events.Count;
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        _requestEvents.AddRange(_sink.Events.Skip(before));
        var code = TryCode(body);
        var errorLog = _sink.Events.Any(e => e.Level >= LogEventLevel.Error);
        output.WriteLine($"MEASURE | {variant} | {(int)response.StatusCode} | {code ?? "-"} | errorLog={errorLog} | {Trim(body)}");
        return (response.StatusCode, body);
    }

    private static string? TryCode(string body)
    {
        var extensions = TryError(body) is { } error && error.TryGetProperty("extensions", out var e) ? e : (JsonElement?)null;
        return extensions is { } ext && ext.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static string Trim(string body) => body.Length <= 160 ? body : body[..160];

    private static JsonElement? TryError(string body)
    {
        try
        {
            var root = JsonDocument.Parse(body).RootElement;
            return root.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0
                ? errors[0].Clone()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void AssertClientError(
        HttpStatusCode status, string body, HttpStatusCode expected, string? message = null, int warnings = 0)
    {
        status.Should().Be(expected, body);
        var root = JsonDocument.Parse(body).RootElement;
        root.TryGetProperty("data", out _).Should().BeFalse(body);
        var error = root.GetProperty("errors").EnumerateArray().Should().ContainSingle().Subject;
        error.GetProperty("extensions").GetProperty("code").GetString().Should().Be("BAD_USER_INPUT", body);
        if (message is not null) error.GetProperty("message").GetString().Should().Be(message);
        error.TryGetProperty("exception", out _).Should().BeFalse();
        AssertLogged(warnings, expected);
    }

    private List<LogEvent> ReadWarnings() => _requestEvents
        .Where(e => e.Level == LogEventLevel.Warning && e.MessageTemplate.Text == ReadWarningTemplate)
        .ToList();

    // No Error entry, and exactly `warnings` body-read Warning entries, each with the status.
    private void AssertLogged(int warnings, HttpStatusCode status)
    {
        _requestEvents.Should().NotContain(e => e.Level >= LogEventLevel.Error);
        var logged = ReadWarnings();
        logged.Should().HaveCount(warnings);
        foreach (var entry in logged)
        {
            entry.RenderMessage().Should().Be($"The request body could not be read (status {(int)status}).");
            ((ScalarValue)entry.Properties["StatusCode"]).Value.Should().Be((int)status);
            entry.Exception.Should().BeOfType<BadHttpRequestException>();
        }
    }

    private void AssertAllClientErrors(List<(HttpStatusCode Status, string Body)> results)
    {
        foreach (var (status, body) in results) AssertClientError(status, body, HttpStatusCode.BadRequest);
    }

    private static string DeepObject(int depth) =>
        string.Concat(Enumerable.Repeat("{\"a\":", depth)) + "1" + new string('}', depth);

    private static string DeepArray(int depth) => new string('[', depth) + "1" + new string(']', depth);

    public static TheoryData<string, string> QueryStringValues()
    {
        var data = new TheoryData<string, string>();
        foreach (var name in new[] { "variables", "extensions" })
        {
            data.Add(name, "not json");
            data.Add(name, "\"text\"");
            data.Add(name, "5");
            data.Add(name, "true");
        }
        data.Add("extensions", "[1,2]");
        data.Add("extensions", "null");
        data.Add("extensions", " ");
        data.Add("variables", " ");
        data.Add("variables", "	");
        data.Add("variables", "[1]");
        data.Add("variables", "[\"x\"]");
        data.Add("variables", "[[1]]");
        data.Add("variables", "[{},1]");
        data.Add("variables", "[null,true]");
        data.Add("variables", DeepObject(65));
        data.Add("extensions", DeepObject(65));
        data.Add("variables", DeepArray(65));
        return data;
    }

    [Theory]
    [MemberData(nameof(QueryStringValues))]
    public async Task Unacceptable_variables_and_extensions_are_a_400_but_variables_may_be_an_array_or_null(
        string name, string value)
    {
        var results = new List<(HttpStatusCode, string)>();
        using var host = Host();
        foreach (var accept in AcceptValues)
        {
            var uri = $"/graphql?query={Uri.EscapeDataString(Query)}&{name}={Uri.EscapeDataString(value)}";
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            Accept(request, accept);

            var (status, body) = await SendAsync(host, request, $"S1 {name}={value} accept={accept ?? "none"}");

            results.Add((status, body));
        }
        AssertAllClientErrors(results);
    }

    [Theory]
    [InlineData("variables", "null")]
    [InlineData("variables", "{}")]
    [InlineData("variables", "[{}]")]
    [InlineData("variables", "[null]")]
    [InlineData("variables", "[{},null]")]
    [InlineData("variables", " {} ")]
    [InlineData("extensions", "{}")]
    [InlineData("extensions", " {} ")]
    public async Task A_query_string_value_of_a_kind_GraphQL_accepts_is_executed(string name, string value)
    {
        using var host = Host();
        foreach (var accept in AcceptValues)
        {
            var uri = $"/graphql?query={Uri.EscapeDataString(Query)}&{name}={Uri.EscapeDataString(value)}";
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            Accept(request, accept);

            var (status, body) = await SendAsync(host, request, $"S1 pin {name}={value} accept={accept ?? "none"}");

            status.Should().Be(HttpStatusCode.OK, body);
            body.Should().Contain("\"__typename\"");
        }
    }

    // Pin: a value that passes the query-string check and that the GraphQL server itself refuses.
    [Theory]
    [InlineData("variables", "[]", HttpStatusCode.BadRequest, "HC0009")]
    public async Task A_query_string_value_the_server_refuses_itself_keeps_the_server_answer_and_logs_no_error(
        string name, string value, HttpStatusCode expected, string serverCode)
    {
        using var host = Host();
        foreach (var accept in AcceptValues)
        {
            var uri = $"/graphql?query={Uri.EscapeDataString(Query)}&{name}={Uri.EscapeDataString(value)}";
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            Accept(request, accept);

            var (status, body) = await SendAsync(host, request, $"S1 server {name}={value} accept={accept ?? "none"}");

            status.Should().Be(expected, body);
            TryCode(body).Should().Be(serverCode, body);
            AssertLogged(0, status);
        }
    }

    private static string Escaped(string value) => Uri.EscapeDataString(value);

    public static TheoryData<string, bool> RawQueryStrings() => new()
    {
        { "variables=%7B%7D&variables=%7B%7D", false },
        { "extensions=%7B%7D&extensions=%7B%7D", false },
        { "variables=null&variables=null", false },
        { "variables=%20&variables=%7B%7D", false },
        { "variables=&variables=%7B%7D", true },
        { "variables=&variables=", true },
        { "extensions=", true },
        { "variables=%5B%7B%7D&variables=%7B%7D%5D", true },
        { "variables=" + Escaped(DeepObject(64)), true },
        { "variables=" + Escaped(DeepObject(65)), false },
    };

    [Theory]
    [MemberData(nameof(RawQueryStrings))]
    public async Task A_repeated_or_blank_or_deep_query_string_value_is_refused_exactly_when_GraphQL_cannot_run_it(
        string rawQuery, bool executes)
    {
        var results = new List<(HttpStatusCode, string)>();
        using var host = Host();
        foreach (var accept in AcceptValues)
        {
            var uri = $"/graphql?query={Uri.EscapeDataString(Query)}&{rawQuery}";
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            Accept(request, accept);

            results.Add(await SendAsync(host, request, $"S1 raw {rawQuery} accept={accept ?? "none"}"));
        }
        if (executes)
            foreach (var (status, body) in results) status.Should().Be(HttpStatusCode.OK, body);
        else
            AssertAllClientErrors(results);
    }

    [Theory]
    [InlineData("[1]", "Number")]
    [InlineData("[\"x\"]", "String")]
    [InlineData("[{},[1]]", "Array")]
    [InlineData("[{},true]", "Boolean")]
    public async Task A_variables_array_with_an_element_that_is_not_an_object_or_null_names_the_kind(
        string value, string kind)
    {
        using var host = Host();
        var uri = $"/graphql?query={Uri.EscapeDataString(Query)}&variables={Uri.EscapeDataString(value)}";

        var (status, body) = await SendAsync(host, new HttpRequestMessage(HttpMethod.Get, uri), $"S1 elements {value}");

        AssertClientError(status, body, HttpStatusCode.BadRequest,
            $"Invalid 'variables' value. Expected array elements to be object or null, but got {kind}.");
    }

    private static MultipartFormDataContent Form(params (string Name, string Value)[] parts)
    {
        var content = new MultipartFormDataContent();
        foreach (var (name, value) in parts) content.Add(new StringContent(value), name);
        return content;
    }

    private static MultipartFormDataContent FormWithFile(params (string Name, string Value)[] parts)
    {
        var content = Form(parts);
        var file = new ByteArrayContent("abc"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(file, "0", "a.txt");
        return content;
    }

    private static ByteArrayContent Raw(string text, string contentType)
    {
        var content = new ByteArrayContent(Encoding.ASCII.GetBytes(text));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return content;
    }

    public static TheoryData<string, bool> MultipartVariants()
    {
        var data = new TheoryData<string, bool>();
        foreach (var name in new[]
                 {
                     "operations-missing", "operations-not-json", "map-not-json", "map-names-missing-path",
                     "file-part-in-no-map-entry", "no-boundary", "truncated", "well-formed",
                 })
            foreach (var preflight in new[] { false, true })
                data.Add(name, preflight);
        return data;
    }

    private static HttpContent MultipartBody(string variant) => variant switch
    {
        "operations-missing" => Form(("map", "{}")),
        "operations-not-json" => Form(("operations", "not json"), ("map", "{}")),
        "map-not-json" => Form(("operations", OperationsJson), ("map", "not json")),
        "map-names-missing-path" => FormWithFile(
            ("operations", "{\"query\":\"{ __typename }\",\"variables\":{\"f\":null}}"),
            ("map", "{\"0\":[\"variables.g\"]}")),
        "file-part-in-no-map-entry" => FormWithFile(("operations", OperationsJson), ("map", "{}")),
        "well-formed" => Form(("operations", OperationsJson), ("map", "{}")),
        "no-boundary" => Raw("hello", "multipart/form-data"),
        "truncated" => Raw(
            "--xyz\r\nContent-Disposition: form-data; name=\"operations\"\r\n\r\n{\"query\"",
            "multipart/form-data; boundary=xyz"),
        _ => throw new ArgumentOutOfRangeException(nameof(variant)),
    };

    private void AssertMultipartRefused(HttpStatusCode status, string body)
    {
        AssertClientError(status, body, HttpStatusCode.UnsupportedMediaType, MultipartRefused);
        JsonDocument.Parse(body).RootElement.GetProperty("errors")[0].GetProperty("extensions")
            .GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    [Theory]
    [MemberData(nameof(MultipartVariants))]
    public async Task Multipart_is_refused_with_415_under_every_Accept_with_or_without_the_preflight_header(
        string variant, bool withPreflight)
    {
        using var host = Host();
        foreach (var accept in AcceptValues)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, GraphQlPath) { Content = MultipartBody(variant) };
            if (withPreflight) request.Headers.Add("GraphQL-Preflight", "1");
            Accept(request, accept);

            var (status, body) = await SendAsync(host, request,
                $"S2 {variant} preflight={withPreflight} accept={accept ?? "none"}");

            AssertMultipartRefused(status, body);
        }
    }

    [Theory]
    [InlineData("POST", "Multipart/Form-Data; BOUNDARY=x")]
    [InlineData("POST", " multipart/form-data ;boundary=\"q\"")]
    [InlineData("POST", "MULTIPART/FORM-DATA")]
    [InlineData("PUT", "multipart/form-data; boundary=x")]
    [InlineData("PATCH", "multipart/form-data; boundary=x")]
    [InlineData("GET", "multipart/form-data; boundary=x")]
    public async Task Multipart_is_refused_whatever_the_casing_parameters_or_method(string method, string contentType)
    {
        using var host = Host();
        var request = new HttpRequestMessage(new HttpMethod(method), GraphQlPath)
        {
            Content = Raw("--x\r\n", contentType),
        };

        var (status, body) = await SendAsync(host, request, $"S2 {method} {contentType}");

        AssertMultipartRefused(status, body);
    }

    [Fact]
    public async Task Multipart_is_refused_without_reading_its_body()
    {
        using var host = Host(new InvalidOperationException("the body must not be read"));
        var request = new HttpRequestMessage(HttpMethod.Post, GraphQlPath) { Content = MultipartBody("well-formed") };

        var (status, body) = await SendAsync(host, request, "S2 multipart body is not read");

        AssertMultipartRefused(status, body);
    }

    [Fact]
    public async Task Multipart_over_the_JSON_cap_is_refused_as_multipart_not_as_too_large()
    {
        using var host = Host();
        var request = new HttpRequestMessage(HttpMethod.Post, GraphQlPath)
        {
            Content = new ByteArrayContent(new byte[5 * 1024 * 1024]),
        };
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=x");

        var (status, body) = await SendAsync(host, request, "S2 multipart over the cap");

        AssertMultipartRefused(status, body);
    }

    // Pin: multipart types other than form-data are not refused by this middleware; the server answers them
    // with an empty 404 and writes neither a Warning nor an Error entry.
    [Theory]
    [InlineData("multipart/mixed; boundary=xyz")]
    [InlineData("multipart/related; boundary=xyz")]
    public async Task Other_multipart_types_are_left_to_the_server_which_answers_404(string contentType)
    {
        using var host = Host();
        foreach (var accept in AcceptValues)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, GraphQlPath)
            {
                Content = Raw(MixedBody, contentType),
            };
            Accept(request, accept);

            var (status, body) = await SendAsync(host, request, $"M1 {contentType} accept={accept ?? "none"}");

            status.Should().Be(HttpStatusCode.NotFound, body);
            AssertLogged(0, status);
        }
    }

    public static TheoryData<int, HttpStatusCode> BodyReadFailures() => new()
    {
        { StatusCodes.Status408RequestTimeout, HttpStatusCode.RequestTimeout },
        { StatusCodes.Status400BadRequest, HttpStatusCode.BadRequest },
    };

    [Theory]
    [MemberData(nameof(BodyReadFailures))]
    public async Task A_body_read_the_server_failed_is_answered_with_the_status_the_server_chose(
        int serverStatus, HttpStatusCode expected)
    {
        using var host = Host(new BadHttpRequestException("server detail", serverStatus));
        var request = new HttpRequestMessage(HttpMethod.Post, GraphQlPath)
        {
            Content = new StringContent(OperationsJson, Encoding.UTF8, "application/json"),
        };

        var (status, body) = await SendAsync(host, request, $"S3 declared-length body read fails with {serverStatus}");

        AssertClientError(status, body, expected, Unreadable, warnings: 1);
    }

    private const string UnreadableTemplate = "The GraphQL request body could not be read.";
    private const string ExceptionDetail = "secret-detail";

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            stream.WriteAsync(bytes, 0, bytes.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private static Exception NonClientFailure(string kind) => kind switch
    {
        "io" => new IOException(ExceptionDetail),
        "bad-request-500" => new BadHttpRequestException(ExceptionDetail, StatusCodes.Status500InternalServerError),
        "invalid-operation" => new InvalidOperationException(ExceptionDetail),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static TheoryData<string, bool> NonClientFailures()
    {
        var data = new TheoryData<string, bool>();
        foreach (var kind in new[] { "io", "bad-request-500", "invalid-operation" })
            foreach (var declared in new[] { true, false })
                data.Add(kind, declared);
        return data;
    }

    private static HttpContent JsonBody(bool declaredLength)
    {
        var bytes = Encoding.UTF8.GetBytes(OperationsJson);
        HttpContent content = declaredLength ? new ByteArrayContent(bytes) : new UnknownLengthContent(bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        return content;
    }

    // The Error entries that carry an exception: the request-completion entry of a 5xx has none.
    private void AssertUnreadableLoggedOnce(Exception failure)
    {
        var errors = _requestEvents.Where(e => e.Level >= LogEventLevel.Error && e.Exception is not null).ToList();
        var entry = errors.Should().ContainSingle().Subject;
        entry.MessageTemplate.Text.Should().Be(UnreadableTemplate);
        entry.Exception.Should().BeSameAs(failure);
        ReadWarnings().Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(NonClientFailures))]
    public async Task A_body_read_that_fails_for_a_reason_that_is_not_the_clients_is_a_GraphQL_internal_error(
        string kind, bool declaredLength)
    {
        var failure = NonClientFailure(kind);
        using var host = Host(failure);
        foreach (var accept in AcceptValues)
        {
            _requestEvents.Clear();
            var request = new HttpRequestMessage(HttpMethod.Post, GraphQlPath) { Content = JsonBody(declaredLength) };
            Accept(request, accept);

            var (status, body) = await SendAsync(host, request,
                $"S3 {kind} declared={declaredLength} accept={accept ?? "none"}");

            status.Should().Be(HttpStatusCode.InternalServerError, body);
            var root = JsonDocument.Parse(body).RootElement;
            root.TryGetProperty("data", out _).Should().BeFalse(body);
            root.TryGetProperty("success", out _).Should().BeFalse(body);
            var error = root.GetProperty("errors").EnumerateArray().Should().ContainSingle().Subject;
            error.GetProperty("message").GetString().Should().Be("An internal error occurred.");
            var extensions = error.GetProperty("extensions");
            extensions.GetProperty("code").GetString().Should().Be("INTERNAL_SERVER_ERROR");
            extensions.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
            error.TryGetProperty("exception", out _).Should().BeFalse();
            body.Should().NotContain(ExceptionDetail);
            AssertUnreadableLoggedOnce(failure);
        }
    }

    // A cancellation is not a failed read: the body read gives it no answer and no log entry, and the
    // exception handler answers it.
    [Fact]
    public async Task A_cancellation_thrown_by_the_body_read_is_not_answered_as_a_failed_read()
    {
        using var host = Host(new OperationCanceledException(ExceptionDetail));
        var client = host.CreateClient();

        using var response = await client.PostAsync(GraphQlPath, JsonBody(declaredLength: true));

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(Unreadable).And.NotContain("BAD_USER_INPUT");
        _sink.Events.Should().NotContain(e => e.MessageTemplate.Text == UnreadableTemplate);
        _sink.Events.Should().ContainSingle(
            e => e.Level == LogEventLevel.Error && e.MessageTemplate.Text == "Unhandled API exception");
    }

    // Pin: percent-encoded bytes that are not UTF-8, an encoded NUL and a raw non-ASCII character reach the
    // check as text, never as invalid UTF-16, and are refused as invalid JSON without an Error entry.
    [Theory]
    [InlineData("variables", "%FF")]
    [InlineData("variables", "%C0%80")]
    [InlineData("variables", "%ED%A0%80")]
    [InlineData("variables", "%F4%90%80%80")]
    [InlineData("variables", "%E2%82")]
    [InlineData("variables", "%00")]
    [InlineData("variables", "é")]
    [InlineData("extensions", "%FF")]
    [InlineData("extensions", "%C0%80")]
    [InlineData("extensions", "%ED%A0%80")]
    [InlineData("extensions", "%F4%90%80%80")]
    [InlineData("extensions", "%E2%82")]
    [InlineData("extensions", "%00")]
    [InlineData("extensions", "é")]
    public async Task A_query_string_value_that_is_not_valid_text_is_refused_as_invalid_JSON(string name, string raw)
    {
        using var host = Host();
        foreach (var accept in AcceptValues)
        {
            _requestEvents.Clear();
            var uri = new Uri($"/graphql?query=%7B__typename%7D&{name}={raw}", UriKind.Relative);
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            Accept(request, accept);

            var (status, body) = await SendAsync(host, request, $"S1 raw {name}={raw} accept={accept ?? "none"}");

            AssertClientError(status, body, HttpStatusCode.BadRequest);
            body.Should().Contain($"Invalid '{name}' value.").And.Contain("but got invalid JSON.");
        }
    }

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status408RequestTimeout)]
    public async Task A_body_read_that_fails_after_the_client_left_ends_without_an_answer_and_logs_nothing(
        int serverStatus)
    {
        await AssertClientLeftAsync(new BadHttpRequestException("server detail", serverStatus));
    }

    [Theory]
    [InlineData("io")]
    [InlineData("bad-request-500")]
    [InlineData("invalid-operation")]
    public async Task A_non_client_body_read_failure_after_the_client_left_ends_without_an_answer_and_logs_nothing(
        string kind)
    {
        await AssertClientLeftAsync(NonClientFailure(kind));
    }

    private async Task AssertClientLeftAsync(Exception failure)
    {
        using var host = Host(failure, clientGone: true);
        var client = host.CreateClient();
        var before = _sink.Events.Count;
        var content = new StringContent(OperationsJson, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(GraphQlPath, content);

        ((int)response.StatusCode).Should().Be(499);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
        _sink.Events.Skip(before).Should().NotContain(e => e.Level >= LogEventLevel.Error);
        _sink.Events.Skip(before).Should().NotContain(
            e => e.Level == LogEventLevel.Warning && e.MessageTemplate.Text == ReadWarningTemplate);
    }

    [Fact]
    public async Task A_body_over_the_cap_from_the_server_is_413_and_writes_no_Warning()
    {
        using var host = Host(new BadHttpRequestException("too large", StatusCodes.Status413PayloadTooLarge));
        var request = new HttpRequestMessage(HttpMethod.Post, GraphQlPath)
        {
            Content = new StringContent(OperationsJson, Encoding.UTF8, "application/json"),
        };

        var (status, body) = await SendAsync(host, request, "S3 body read fails with 413");

        status.Should().Be(HttpStatusCode.RequestEntityTooLarge, body);
        TryCode(body).Should().Be("PAYLOAD_TOO_LARGE", body);
        AssertLogged(0, status);
    }
}
