using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Struo.Infrastructure.Persistence;
using Xunit;

namespace Struo.Tests.Persistence;

public class CancellationAopTests
{
    [Fact]
    public void Cancelled_token_becomes_OperationCanceledException_carrying_that_token()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var error = new SqlSugarException("Connection open error . A task was canceled");

        var result = CancellationAop.Translate(error, cts.Token);

        result.Should().NotBeNull();
        result!.CancellationToken.Should().Be(cts.Token);
        result.InnerException.Should().BeSameAs(error);
        result.Message.Should().Be(error.Message);
    }

    [Fact]
    public void Inner_OperationCanceledException_is_translated_when_the_token_is_not_cancelled()
    {
        using var cts = new CancellationTokenSource();
        var error = new InvalidOperationException("failed", new OperationCanceledException(cts.Token));

        var result = CancellationAop.Translate(error, CancellationToken.None);

        result.Should().NotBeNull();
        result!.InnerException.Should().BeSameAs(error);
    }

    [Fact]
    public void Unrelated_error_with_an_uncancelled_token_is_not_translated()
    {
        using var cts = new CancellationTokenSource();

        CancellationAop.Translate(new SqlSugarException("syntax error"), cts.Token).Should().BeNull();
        CancellationAop.Translate(new SqlSugarException("syntax error"), null).Should().BeNull();
    }

    // SqlConnection rejects an already-cancelled token before any network I/O, so no server is needed.
    [Fact]
    public async Task SqlServer_client_turns_a_failed_command_under_a_cancelled_token_into_OperationCanceledException()
    {
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions
            {
                DbType = StruoDbType.SqlServer,
                ConnectionString = "Server=127.0.0.1,1;Database=x_test;User Id=u;Password=p;Connect Timeout=1;TrustServerCertificate=true",
            },
            new TestCurrentUserAccessor(null));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.Queryable<Role>().ToListAsync(cts.Token));

        thrown.InnerException.Should().BeOfType<SqlSugarException>();
    }
}
