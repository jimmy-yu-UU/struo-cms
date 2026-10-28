namespace Struo.Tests.Support;

/// <summary>
/// Hand-rolled <see cref="TimeProvider"/> for deterministic clock tests: starts at the real wall-clock
/// time (so any absolute expiration a caller derives from it still lands comfortably in the real
/// future) and only moves forward when <see cref="Advance"/> is called explicitly — never with real
/// time. Lets a test simulate "N seconds later" in zero wall-clock time instead of an actual
/// <c>Task.Delay</c>.
/// </summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
