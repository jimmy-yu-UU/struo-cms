// tests/Struo.Tests/GraphQl/GraphQlMalformedRequestTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Language;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog.Core;
using Serilog.Events;
using Struo.Api.GraphQl;
using Struo.Tests.Api;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>A GraphQL request whose body the server cannot accept is a client error, answered the same way with no <c>Accept</c>, with <c>*/*</c> and with <c>application/json</c>.</summary>
[Collection("ApiIntegration")]
public class GraphQlMalformedRequestTests(ApiFactory factory)
{
    private const string NoOperation = "Request must contain either a query or a document id.";
    private const string NotJson = "Invalid JSON document.";

    private readonly CapturingLogSink _sink = new();

    public static TheoryData<string, string?, string> Bodies()
    {
        var data = new TheoryData<string, string?, string>();
        foreach (var accept in new string?[] { null, "*/*", "application/json" })
        {
            data.Add("{\"query\":\"\"}", accept, NoOperation);
            data.Add("{}", accept, NoOperation);
            data.Add("not json", accept, NotJson);
        }

        data.Add("{\"query\":null}", null, NoOperation);
        data.Add("{\"query\":5}", null, "Invalid 'query' value. Expected string or null, but got Number.");
        data.Add("{\"query\":\"{ __typename }\",\"variables\":5}", null,
            "Invalid 'variables' value. Expected object, array, or null, but got Number.");
        data.Add("{\"query\":\"{ __typename }\",\"extensions\":5}", null,
            "Invalid 'extensions' value. Expected object or null, but got Number.");
        data.Add("{\"query\":\"{ __typename }\",\"operationName\":5}", null,
            "Invalid 'operationName' value. Expected string or null, but got Number.");
        return data;
    }

    private WebApplicationFactory<Program> Host() => factory.WithWebHostBuilder(b =>
        b.ConfigureServices(s => s.AddSingleton<ILogEventSink>(_sink)));

    private static async Task<(HttpStatusCode Status, JsonElement Root)> PostAsync(
        WebApplicationFactory<Program> host, string body, string? accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/graphql")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (accept is not null) request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(accept));
        using var response = await host.CreateClient().SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    [Theory]
    [MemberData(nameof(Bodies))]
    public async Task A_request_without_an_operation_or_a_json_body_is_a_400_client_error(
        string body, string? accept, string message)
    {
        using var host = Host();

        var (status, root) = await PostAsync(host, body, accept);

        status.Should().Be(HttpStatusCode.BadRequest, root.ToString());
        root.TryGetProperty("data", out _).Should().BeFalse(root.ToString());
        var error = root.GetProperty("errors").EnumerateArray().Should().ContainSingle().Subject;
        error.GetProperty("message").GetString().Should().Be(message);
        error.GetProperty("extensions").GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
        error.GetProperty("extensions").GetProperty("traceId").GetString().Should().HaveLength(32);
        error.TryGetProperty("exception", out _).Should().BeFalse();
        _sink.Events.Should().NotContain(e => e.Level >= LogEventLevel.Error);
    }
}

public class StruoErrorFilterRequestErrorTests
{
    private static StruoErrorFilter Filter(ILogger<StruoErrorFilter>? logger = null) =>
        new(logger ?? NullLoggerFactory.Instance.CreateLogger<StruoErrorFilter>(),
            new HttpContextAccessor { HttpContext = null });

    [Fact]
    public void A_JsonException_without_a_path_is_an_internal_error_and_is_logged_as_an_error()
    {
        var logger = new RecordingLogger<StruoErrorFilter>();
        var error = ErrorBuilder.New().SetMessage("x").SetException(new JsonException("server fault")).Build();

        var mapped = Filter(logger).OnError(error);

        mapped.Code.Should().Be("INTERNAL_SERVER_ERROR");
        mapped.Message.Should().Be("An internal error occurred.");
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(408)]
    public void A_request_body_the_server_rejected_is_BAD_USER_INPUT_with_a_fixed_message(int status)
    {
        var logger = new RecordingLogger<StruoErrorFilter>();
        var error = ErrorBuilder.New().SetMessage("x")
            .SetException(new BadHttpRequestException("server detail", status)).Build();

        var mapped = Filter(logger).OnError(error);

        mapped.Code.Should().Be("BAD_USER_INPUT");
        mapped.Message.Should().Be("The request body could not be read.");
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public void A_BadHttpRequestException_that_is_not_a_client_error_is_an_internal_error()
    {
        var error = ErrorBuilder.New().SetMessage("x")
            .SetException(new BadHttpRequestException("server detail", 500)).Build();

        Filter().OnError(error).Code.Should().Be("INTERNAL_SERVER_ERROR");
    }

    [Fact]
    public void A_request_error_the_parser_raised_is_a_client_error_without_the_exception()
    {
        var error = ErrorBuilder.New()
            .SetMessage("An internal error occurred.")
            .SetException(new InvalidGraphQLRequestException("Invalid JSON document."))
            .Build();

        var mapped = Filter().OnError(error);

        mapped.Code.Should().Be("BAD_USER_INPUT");
        mapped.Message.Should().Be("Invalid JSON document.");
        mapped.Exception.Should().BeNull();
    }

    [Fact]
    public void The_same_exception_on_an_error_with_a_path_stays_an_internal_error()
    {
        var error = ErrorBuilder.New()
            .SetMessage("original")
            .SetPath(HotChocolate.Path.Root.Append("fxArticles"))
            .SetException(new InvalidGraphQLRequestException("from a resolver"))
            .Build();

        var mapped = Filter().OnError(error);

        mapped.Code.Should().Be("INTERNAL_SERVER_ERROR");
        mapped.Message.Should().Be("An internal error occurred.");
    }

    [Fact]
    public void Any_other_exception_without_a_path_stays_an_internal_error()
    {
        var error = ErrorBuilder.New().SetMessage("x").SetException(new InvalidOperationException("boom")).Build();

        Filter().OnError(error).Code.Should().Be("INTERNAL_SERVER_ERROR");
    }
}
