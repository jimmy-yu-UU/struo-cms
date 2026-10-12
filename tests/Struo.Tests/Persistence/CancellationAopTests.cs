using AwesomeAssertions;
using SqlSugar;
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
}
