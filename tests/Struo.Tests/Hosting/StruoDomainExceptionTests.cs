// tests/Struo.Tests/Hosting/StruoDomainExceptionTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Types;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Struo.Api.Http;
using Struo.Domain;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Hosting;

/// <summary>
/// A fork defines its own client-facing error by deriving from <see cref="StruoDomainException"/>:
/// its code, status and message reach the client unchanged on REST and GraphQL.
/// </summary>
[Collection("ApiIntegration")]
public class StruoDomainExceptionTests
{
    public sealed class QuotaExceededException(string message)
        : StruoDomainException("ACME_QUOTA_EXCEEDED", 429, message);

    [ExtendObjectType("Query")]
    public sealed class QuotaQuery
    {
        public string GetQuota() => throw new QuotaExceededException("Daily quota used up.");
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(499)]
    public void A_client_error_status_is_accepted(int status)
    {
        var exception = new TestException("ACME_X", status, "text");

        exception.Status.Should().Be(status);
        exception.Code.Should().Be("ACME_X");
        exception.Message.Should().Be("text");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    [InlineData(399)]
    [InlineData(500)]
    [InlineData(503)]
    public void A_status_outside_400_to_499_is_rejected_at_construction(int status)
    {
        var create = () => new TestException("ACME_X", status, "text");

        create.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*status*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_blank_code_is_rejected(string code)
    {
        var create = () => new TestException(code, 400, "text");

        create.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_shared_map_returns_the_fork_code_message_and_status(bool authenticated)
    {
        var exception = new QuotaExceededException("Daily quota used up.");

        var (code, message) = DomainErrorMap.Map(exception, authenticated);

        code.Should().Be("ACME_QUOTA_EXCEEDED");
        message.Should().Be("Daily quota used up.");
        DomainErrorMap.StatusFor(exception, code).Should().Be(429);
    }

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public async Task A_REST_endpoint_returns_the_fork_error_in_the_envelope()
    {
        await using var host = await ForkHost.StartAsync(
            mapEndpoints: app => app.MapGet("/fork/quota", string () =>
                throw new QuotaExceededException("Daily quota used up.")));

        var response = await host.CreateClient().GetAsync("/fork/quota");
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var error = doc.RootElement.GetProperty("error");

        response.StatusCode.Should().Be((HttpStatusCode)429, json);
        error.GetProperty("code").GetString().Should().Be("ACME_QUOTA_EXCEEDED");
        error.GetProperty("message").GetString().Should().Be("Daily quota used up.");
    }

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public async Task A_GraphQL_field_returns_the_fork_error_code_and_message()
    {
        await using var host = await ForkHost.StartAsync(cms =>
            cms.ConfigureGraphQl(gql => gql.AddTypeExtension<QuotaQuery>()));

        var response = await host.CreateClient().PostAsJsonAsync("/graphql", new { query = "{ quota }" });
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var error = doc.RootElement.GetProperty("errors")[0];

        error.GetProperty("message").GetString().Should().Be("Daily quota used up.");
        error.GetProperty("extensions").GetProperty("code").GetString().Should().Be("ACME_QUOTA_EXCEEDED");
    }

    private sealed class TestException(string code, int status, string message)
        : StruoDomainException(code, status, message);
}
