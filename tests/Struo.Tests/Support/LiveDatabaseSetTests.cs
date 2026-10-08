using AwesomeAssertions;
using Struo.Application.Configuration;
using Xunit;

namespace Struo.Tests.Support;

public sealed class LiveDatabaseSetTests
{
    private static readonly LiveBackend Backend = LiveBackend.Get("PostgreSQL");

    private sealed class FakeSet(Func<TestBackendKind, string, ITestDatabase> create)
        : LiveDatabaseSet("fake", create);

    private sealed class FakeDatabase : ITestDatabase
    {
        public int Disposed { get; private set; }
        public StruoDbType DbType => StruoDbType.PostgreSQL;
        public string ConnectionString => "Host=x;Database=fake_test";
        public string Name => "fake_test";
        public void Dispose() => Disposed++;
    }

    [Fact]
    public void A_failed_create_is_attempted_once_and_rethrown_to_later_callers()
    {
        var attempts = 0;
        using var set = new FakeSet((_, _) =>
        {
            attempts++;
            throw new InvalidOperationException("server unreachable");
        });

        for (var i = 0; i < 3; i++)
        {
            var act = () => set.ConnectionFor(Backend);
            act.Should().Throw<InvalidOperationException>().WithMessage("server unreachable");
        }

        attempts.Should().Be(1);
    }

    [Fact]
    public void A_created_database_is_reused_and_dropped_once_on_dispose()
    {
        var database = new FakeDatabase();
        var creates = 0;
        var set = new FakeSet((_, _) =>
        {
            creates++;
            return database;
        });

        set.ConnectionFor(Backend).Should().Be(database.ConnectionString);
        set.ConnectionFor(Backend).Should().Be(database.ConnectionString);
        IDisposable disposable = set;
        disposable.Dispose();
        disposable.Dispose();

        creates.Should().Be(1);
        database.Disposed.Should().Be(1);
    }
}
