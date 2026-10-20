using AwesomeAssertions;
using Struo.Application.Files;
using Xunit;

namespace Struo.Tests.Files;

public class ImageTransformGateTests
{
    private static ImageTransformGate GateWith(int slots, int timeoutSeconds)
    {
        var options = new FileStorageOptions();
        options.ImageTransform.MaxConcurrentTransforms = slots;
        options.ImageTransform.QueueTimeoutSeconds = timeoutSeconds;
        return new ImageTransformGate(options);
    }

    [Fact]
    public async Task Anonymous_callers_hold_at_most_one_slot_fewer_than_the_total()
    {
        var gate = GateWith(slots: 3, timeoutSeconds: 0);

        using var first = await gate.TryEnterAsync(anonymous: true, CancellationToken.None);
        using var second = await gate.TryEnterAsync(anonymous: true, CancellationToken.None);
        var third = await gate.TryEnterAsync(anonymous: true, CancellationToken.None);

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        third.Should().BeNull();
    }

    [Fact]
    public async Task An_authenticated_caller_can_use_the_slot_anonymous_callers_leave_free()
    {
        var gate = GateWith(slots: 2, timeoutSeconds: 0);
        using var anonymous = await gate.TryEnterAsync(anonymous: true, CancellationToken.None);

        using var authenticated = await gate.TryEnterAsync(anonymous: false, CancellationToken.None);
        var overflow = await gate.TryEnterAsync(anonymous: false, CancellationToken.None);

        authenticated.Should().NotBeNull();
        overflow.Should().BeNull();
    }

    [Fact]
    public async Task Authenticated_callers_are_capped_by_the_total_not_the_anonymous_cap()
    {
        var gate = GateWith(slots: 2, timeoutSeconds: 0);

        using var first = await gate.TryEnterAsync(anonymous: false, CancellationToken.None);
        using var second = await gate.TryEnterAsync(anonymous: false, CancellationToken.None);

        first.Should().NotBeNull();
        second.Should().NotBeNull();
    }

    [Fact]
    public async Task Disposing_an_anonymous_lease_frees_its_anonymous_share()
    {
        var gate = GateWith(slots: 2, timeoutSeconds: 0);
        var first = await gate.TryEnterAsync(anonymous: true, CancellationToken.None);
        (await gate.TryEnterAsync(anonymous: true, CancellationToken.None)).Should().BeNull();

        first!.Dispose();
        first.Dispose();   // a second dispose must not release twice

        using var again = await gate.TryEnterAsync(anonymous: true, CancellationToken.None);
        again.Should().NotBeNull();
        (await gate.TryEnterAsync(anonymous: true, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task An_anonymous_caller_turned_away_by_a_full_gate_does_not_leak_its_anonymous_share()
    {
        var gate = GateWith(slots: 2, timeoutSeconds: 0);
        var holderA = await gate.TryEnterAsync(anonymous: false, CancellationToken.None);
        var holderB = await gate.TryEnterAsync(anonymous: false, CancellationToken.None);

        (await gate.TryEnterAsync(anonymous: true, CancellationToken.None)).Should().BeNull();

        holderA!.Dispose();
        holderB!.Dispose();
        using var anonymous = await gate.TryEnterAsync(anonymous: true, CancellationToken.None);
        anonymous.Should().NotBeNull();
    }

    [Fact]
    public async Task A_waiting_anonymous_caller_gets_the_slot_when_another_anonymous_caller_leaves()
    {
        var gate = GateWith(slots: 2, timeoutSeconds: 60);
        var holder = await gate.TryEnterAsync(anonymous: true, CancellationToken.None);

        var waiting = gate.TryEnterAsync(anonymous: true, CancellationToken.None);
        waiting.IsCompleted.Should().BeFalse();
        holder!.Dispose();

        using var lease = await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        lease.Should().NotBeNull();
    }

    [Fact]
    public async Task A_cancelled_anonymous_wait_does_not_leak_its_anonymous_share()
    {
        var gate = GateWith(slots: 2, timeoutSeconds: 60);
        var holderA = await gate.TryEnterAsync(anonymous: false, CancellationToken.None);
        var holderB = await gate.TryEnterAsync(anonymous: false, CancellationToken.None);
        using var cts = new CancellationTokenSource();

        var waiting = gate.TryEnterAsync(anonymous: true, cts.Token);
        await cts.CancelAsync();
        var act = async () => await waiting;
        await act.Should().ThrowAsync<OperationCanceledException>();

        holderA!.Dispose();
        holderB!.Dispose();
        using var anonymous = await gate.TryEnterAsync(anonymous: true, CancellationToken.None);
        anonymous.Should().NotBeNull();
    }
}
