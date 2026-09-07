using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Struo.Application.Changes;
using Struo.Infrastructure.Changes;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Changes;

public class ItemChangeNotifierTests
{
    private static readonly IReadOnlyList<ItemChange> OneChange = [new("article", "a1", ItemChangeKind.Created)];

    private sealed class ThrowingListener : IItemChangeListener
    {
        public Task OnChangedAsync(IReadOnlyList<ItemChange> changes, CancellationToken ct = default) =>
            throw new InvalidOperationException("index at 10.0.0.5 refused");
    }

    [Fact]
    public async Task Calls_every_listener_in_registration_order_with_the_same_batch()
    {
        var a = new RecordingItemChangeListener(); var b = new RecordingItemChangeListener();
        var order = new List<string>();
        var first = new RecordingItemChangeListener { OnCall = _ => { order.Add("first"); return Task.CompletedTask; } };
        var second = new RecordingItemChangeListener { OnCall = _ => { order.Add("second"); return Task.CompletedTask; } };
        var notifier = new ItemChangeNotifier([first, second, a, b], new ListLogger<ItemChangeNotifier>());
        await notifier.NotifyAsync(OneChange);
        order.Should().Equal("first", "second");
        a.Calls.Should().ContainSingle().Which.Should().BeSameAs(OneChange);
        b.Calls.Should().ContainSingle().Which.Should().BeSameAs(OneChange);
    }

    [Fact]
    public async Task A_failing_listener_is_logged_at_error_and_does_not_stop_the_others_or_throw()
    {
        var logger = new ListLogger<ItemChangeNotifier>();
        var after = new RecordingItemChangeListener();
        var notifier = new ItemChangeNotifier([new ThrowingListener(), after], logger);
        var changes = new List<ItemChange>
        {
            new("article", "a1", ItemChangeKind.Created), new("article", "a2", ItemChangeKind.Purged), new("tag", "t1", ItemChangeKind.Purged),
        };
        await FluentActions.Awaiting(() => notifier.NotifyAsync(changes)).Should().NotThrowAsync();
        after.Calls.Should().ContainSingle();
        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Message.Should().Contain("ThrowingListener").And.Contain("3 change(s)").And.Contain("Created=1").And.Contain("Purged=2");
    }

    [Fact]
    public async Task Empty_batch_or_no_listeners_is_a_no_op()
    {
        var logger = new ListLogger<ItemChangeNotifier>();
        var listener = new RecordingItemChangeListener();
        await new ItemChangeNotifier([listener], logger).NotifyAsync([]);
        listener.Calls.Should().BeEmpty();
        await FluentActions.Awaiting(() => new ItemChangeNotifier([], logger).NotifyAsync(OneChange)).Should().NotThrowAsync();
        logger.Entries.Should().BeEmpty();
    }
}
