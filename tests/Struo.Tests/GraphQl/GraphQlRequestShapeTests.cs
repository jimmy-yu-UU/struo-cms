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
/// Requests to <c>/graphql</c> whose query string, multipart body or body read is bad: each is a client
/// error answered the same way under every <c>Accept</c> value, and none is logged as an error.
/// </summary>
[Collection("ApiIntegration")]
public class GraphQlRequestShapeTests(ApiFactory factory, ITestOutputHelper output)
{
    private const string Unreadable = "The request body could not be read.";
    private const string Query = "{ __typename }";
    private const string OperationsJson = "{\"query\":\"{ __typename }\"}";

    private static readonly string?[] AcceptValues =
        [null, "*/*", "application/json", "application/graphql-response+json"];

    private readonly CapturingLogSink _sink = new();

    private WebApplicationFactory<Program> Host(Exception? bodyFailure = null) => factory.WithWebHostBuilder(b =>
        b.ConfigureServices(s =>
        {
            s.AddSingleton<ILogEventSink>(_sink);
            if (bodyFailure is not null)
                s.AddSingleton<IStartupFilter>(new FailingBodyPathFilter("/graphql", bodyFailure));
        }));

    private static void Accept(HttpRequestMessage request, string? accept)
    {
        if (accept is not null) request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(accept));
    }

    private async Task<(HttpStatusCode Status, string Body)> SendAsync(
        WebApplicationFactory<Program> host, HttpRequestMessage request, string variant)
    {
        using var response = await host.CreateClient().SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
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

    private void AssertClientError(HttpStatusCode status, string body, HttpStatusCode expected, string? message = null)
    {
        status.Should().Be(expected, body);
        var root = JsonDocument.Parse(body).RootElement;
        root.TryGetProperty("data", out _).Should().BeFalse(body);
        var error = root.GetProperty("errors").EnumerateArray().Should().ContainSingle().Subject;
        error.GetProperty("extensions").GetProperty("code").GetString().Should().Be("BAD_USER_INPUT", body);
        if (message is not null) error.GetProperty("message").GetString().Should().Be(message);
        error.TryGetProperty("exception", out _).Should().BeFalse();
        _sink.Events.Should().NotContain(e => e.Level >= LogEventLevel.Error);
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
        foreach (var accept in AcceptValues)
        {
            using var host = Host();
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
        foreach (var accept in AcceptValues)
        {
            using var host = Host();
            var uri = $"/graphql?query={Uri.EscapeDataString(Query)}&{name}={Uri.EscapeDataString(value)}";
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            Accept(request, accept);

            var (status, body) = await SendAsync(host, request, $"S1 pin {name}={value} accept={accept ?? "none"}");

            status.Should().Be(HttpStatusCode.OK, body);
            body.Should().Contain("\"__typename\"");
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
        foreach (var accept in AcceptValues)
        {
            using var host = Host();
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

    public static TheoryData<string> MultipartVariants()
    {
        var data = new TheoryData<string>();
        foreach (var name in new[] { "operations-missing", "operations-not-json", "map-not-json", "map-names-missing-path", "file-part-in-no-map-entry", "no-boundary", "truncated", "well-formed" })
            data.Add(name);
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

    [Theory]
    [MemberData(nameof(MultipartVariants))]
    public async Task Multipart_without_the_GraphQL_preflight_header_is_refused_with_400_under_every_Accept(string variant)
    {
        var results = new List<(HttpStatusCode, string)>();
        foreach (var accept in AcceptValues)
        {
            using var host = Host();
            var request = new HttpRequestMessage(HttpMethod.Post, "/graphql") { Content = MultipartBody(variant) };
            Accept(request, accept);

            var (status, body) = await SendAsync(host, request, $"S2 {variant} accept={accept ?? "none"}");

            results.Add((status, body));
        }
        foreach (var (status, body) in results)
        {
            status.Should().Be(HttpStatusCode.BadRequest, body);
            TryCode(body).Should().Be("HC0077", body);
        }
        _sink.Events.Should().NotContain(e => e.Level >= LogEventLevel.Error);
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
        var request = new HttpRequestMessage(HttpMethod.Post, "/graphql")
        {
            Content = new StringContent(OperationsJson, Encoding.UTF8, "application/json"),
        };

        var (status, body) = await SendAsync(host, request, $"S3 declared-length body read fails with {serverStatus}");

        AssertClientError(status, body, expected, Unreadable);
    }

    [Fact]
    public async Task A_body_read_failure_that_is_not_a_client_error_is_an_internal_error_with_an_error_log()
    {
        using var host = Host(new BadHttpRequestException("server detail", StatusCodes.Status500InternalServerError));
        var request = new HttpRequestMessage(HttpMethod.Post, "/graphql")
        {
            Content = new StringContent(OperationsJson, Encoding.UTF8, "application/json"),
        };

        var (status, body) = await SendAsync(host, request, "S3 declared-length body read fails with 500");

        status.Should().Be(HttpStatusCode.InternalServerError, body);
        JsonDocument.Parse(body).RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("INTERNAL_SERVER_ERROR", body);
        _sink.Events.Should().Contain(e => e.Level >= LogEventLevel.Error);
    }
}
