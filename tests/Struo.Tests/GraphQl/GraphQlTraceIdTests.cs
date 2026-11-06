using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog.Core;
using Struo.Api.GraphQl;
using Struo.Domain.Query;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.GraphQl;

public class StruoErrorFilterTraceIdTests
{
    private static IError Wrap(Exception ex) =>
        ErrorBuilder.New().SetMessage("original").SetException(ex).Build();

    private static StruoErrorFilter Filter(HttpContext? context) =>
        new(NullLoggerFactory.Instance.CreateLogger<StruoErrorFilter>(),
            new HttpContextAccessor { HttpContext = context });

    [Fact]
    public void Mapped_error_carries_the_trace_id_extension()
    {
        using var activity = new Activity("test").Start();

        var mapped = Filter(new DefaultHttpContext()).OnError(Wrap(new QueryException("bad")));

        mapped.Extensions!["traceId"].Should().Be(activity.TraceId.ToHexString());
    }

    [Fact]
    public void Masked_internal_error_carries_the_trace_id_extension()
    {
        using var activity = new Activity("test").Start();

        var mapped = Filter(new DefaultHttpContext()).OnError(Wrap(new InvalidOperationException("secret")));

        mapped.Code.Should().Be("INTERNAL_SERVER_ERROR");
        mapped.Extensions!["traceId"].Should().Be(activity.TraceId.ToHexString());
    }

    [Fact]
    public void Error_without_an_exception_carries_the_trace_id_extension()
    {
        using var activity = new Activity("test").Start();
        var validation = ErrorBuilder.New().SetMessage("depth").Build();

        var mapped = Filter(new DefaultHttpContext()).OnError(validation);

        mapped.Extensions!["traceId"].Should().Be(activity.TraceId.ToHexString());
    }

    [Fact]
    public void No_trace_extension_when_there_is_no_request_and_no_activity()
    {
        Activity.Current = null;

        var mapped = Filter(null).OnError(Wrap(new QueryException("bad")));

        (mapped.Extensions?.ContainsKey("traceId") ?? false).Should().BeFalse();
    }
}

[Collection("ApiIntegration")]
public class GraphQlTraceIdEndpointTests(ApiFactory factory)
{
    private readonly CapturingLogSink _sink = new();

    private WebApplicationFactory<Program> Host() => factory.WithWebHostBuilder(b =>
        b.ConfigureServices(s => s.AddSingleton<ILogEventSink>(_sink)));

    private static JsonElement FirstError(string json) =>
        JsonDocument.Parse(json).RootElement.TryGetProperty("errors", out var errors)
            ? errors[0].Clone()
            : throw new InvalidOperationException(json);

    [Fact]
    public async Task Validation_error_carries_the_trace_id_the_request_log_records()
    {
        const int nested = 25;
        var query = "{ fxCategories { items { " + string.Concat(Enumerable.Repeat("parent { ", nested))
            + "name" + string.Concat(Enumerable.Repeat(" }", nested)) + " } } }";

        using var host = Host();
        var response = await host.CreateClient().PostAsJsonAsync("/graphql", new { query });

        var vbody = await response.Content.ReadAsStringAsync();
        Assert.True(FirstError(vbody).TryGetProperty("extensions", out var ex) && ex.TryGetProperty("traceId", out _), vbody);
        var traceId = FirstError(vbody).GetProperty("extensions").GetProperty("traceId").GetString();
        traceId.Should().HaveLength(32);
        (await _sink.WaitForRequestLogAsync(traceId!)).Should().NotBeNull();
    }

    [Fact]
    public async Task Resolver_error_carries_the_trace_id_the_request_log_records()
    {
        using var host = Host();
        var response = await host.CreateClient().PostAsJsonAsync("/graphql", new
        {
            query = "{ fxArticles(filter: { ghost: { _eq: \"x\" } }) { items { id } } }",
        });

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(FirstError(body).TryGetProperty("extensions", out var ex) && ex.TryGetProperty("traceId", out _), body);
        var traceId = FirstError(body).GetProperty("extensions").GetProperty("traceId").GetString();
        traceId.Should().HaveLength(32, body);
        (await _sink.WaitForRequestLogAsync(traceId!)).Should().NotBeNull();
    }
}
