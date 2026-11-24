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

    private static readonly string?[] AcceptValues = [null, "*/*", "application/json"];

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
        return data;
    }

    [Theory]
    [MemberData(nameof(QueryStringValues))]
    public async Task A_query_string_value_that_is_not_a_JSON_object_is_a_400_under_every_Accept(string name, string value)
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
    [InlineData("extensions", "{}")]
    public async Task A_query_string_value_of_a_kind_GraphQL_accepts_is_executed(string name, string value)
    {
        using var host = Host();
        var uri = $"/graphql?query={Uri.EscapeDataString(Query)}&{name}={Uri.EscapeDataString(value)}";

        var (status, body) = await SendAsync(host, new HttpRequestMessage(HttpMethod.Get, uri), $"S1 pin {name}={value}");

        status.Should().Be(HttpStatusCode.OK, body);
        body.Should().Contain("\"__typename\"");
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
}
