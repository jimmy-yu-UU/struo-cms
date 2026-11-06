using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Struo.Api.Http;
using Xunit;

namespace Struo.Tests.Api;

public class RequestTraceIdTests
{
    [Fact]
    public void Uses_the_current_activity_trace_id_when_an_activity_exists()
    {
        using var activity = new Activity("test").Start();
        var context = new DefaultHttpContext { TraceIdentifier = "fallback-id" };

        var id = RequestTraceId.For(context);

        id.Should().Be(activity.TraceId.ToHexString());
        id.Should().HaveLength(32);
    }

    [Fact]
    public void Falls_back_to_the_http_trace_identifier_without_an_activity()
    {
        Activity.Current = null;
        var context = new DefaultHttpContext { TraceIdentifier = "fallback-id" };

        RequestTraceId.For(context).Should().Be("fallback-id");
    }

    [Fact]
    public void Is_null_without_an_activity_or_a_context()
    {
        Activity.Current = null;

        RequestTraceId.For(null).Should().BeNull();
    }

    [Fact]
    public void Is_the_activity_trace_id_without_a_context()
    {
        using var activity = new Activity("test").Start();

        RequestTraceId.For(null).Should().Be(activity.TraceId.ToHexString());
    }
}
