using AwesomeAssertions;
using Struo.Infrastructure.Query;
using Xunit;

namespace Struo.Tests.Query;

public class GenericDispatcherTests
{
    // Private generic instance methods play the role the repository's *GenericAsync helpers play.
    private Task<string> Describe<T>(int suffix) => Task.FromResult(typeof(T).Name + suffix);
    private Task<string> Pair<T1, T2>(int suffix) => Task.FromResult(typeof(T1).Name + "+" + typeof(T2).Name + suffix);
    private Task<string> NotGeneric(int suffix) => Task.FromResult(suffix.ToString());

    // Private generic STATIC methods play the role FilterTranslator.Subquery's Project/Guard/ToSql
    // play: no receiver, dispatched through a delegate type with no leading receiver parameter.
    private static string DescribeStatic<T>(int suffix) => typeof(T).Name + suffix;
    private static string PairStatic<T1, T2>(int suffix) => typeof(T1).Name + "+" + typeof(T2).Name + suffix;

    private static readonly GenericDispatcher<Func<GenericDispatcherTests, int, Task<string>>> DescribeDispatcher =
        new(typeof(GenericDispatcherTests), nameof(Describe), [typeof(int)]);

    private static readonly BiGenericDispatcher<Func<GenericDispatcherTests, int, Task<string>>> PairDispatcher =
        new(typeof(GenericDispatcherTests), nameof(Pair), [typeof(int)]);

    private static readonly GenericDispatcher<Func<int, string>> DescribeStaticDispatcher =
        new(typeof(GenericDispatcherTests), nameof(DescribeStatic), [typeof(int)]);

    private static readonly BiGenericDispatcher<Func<int, string>> PairStaticDispatcher =
        new(typeof(GenericDispatcherTests), nameof(PairStatic), [typeof(int)]);

    [Fact]
    public async Task For_returns_a_delegate_that_invokes_the_closed_method()
    {
        var result = await DescribeDispatcher.For(typeof(int))(this, 7);
        result.Should().Be("Int327");
    }

    [Fact]
    public void For_caches_per_entity_type()
    {
        var a = DescribeDispatcher.For(typeof(int));
        var b = DescribeDispatcher.For(typeof(int));
        var c = DescribeDispatcher.For(typeof(string));

        b.Should().BeSameAs(a);
        c.Should().NotBeSameAs(a);
    }

    [Fact]
    public async Task Bi_dispatcher_closes_both_type_parameters_and_caches_per_pair()
    {
        var first = PairDispatcher.For(typeof(int), typeof(string));
        var again = PairDispatcher.For(typeof(int), typeof(string));
        var swapped = PairDispatcher.For(typeof(string), typeof(int));

        again.Should().BeSameAs(first);
        swapped.Should().NotBeSameAs(first);
        (await first(this, 1)).Should().Be("Int32+String1");
        (await swapped(this, 1)).Should().Be("String+Int321");
    }

    // A static worker's delegate type carries no receiver parameter — GenericDispatcher's cached
    // MakeGenericMethod(...).CreateDelegate<TDelegate>() call must adapt to the static shape, not
    // just the open-instance one the tests above exercise.
    [Fact]
    public void Static_generic_dispatcher_invokes_the_closed_method_with_no_receiver()
    {
        var result = DescribeStaticDispatcher.For(typeof(int))(7);
        result.Should().Be("Int327");
    }

    [Fact]
    public void Static_bi_dispatcher_closes_both_type_parameters_and_caches_per_pair()
    {
        var first = PairStaticDispatcher.For(typeof(int), typeof(string));
        var again = PairStaticDispatcher.For(typeof(int), typeof(string));
        var swapped = PairStaticDispatcher.For(typeof(string), typeof(int));

        again.Should().BeSameAs(first);
        swapped.Should().NotBeSameAs(first);
        first(1).Should().Be("Int32+String1");
        swapped(1).Should().Be("String+Int321");
    }

    [Fact]
    public void Constructor_throws_when_the_method_is_missing()
    {
        var act = () => new GenericDispatcher<Func<GenericDispatcherTests, int, Task<string>>>(
            typeof(GenericDispatcherTests), "Nope", [typeof(int)]);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Nope*");
    }

    [Fact]
    public void Constructor_throws_with_the_instance_or_static_message_when_the_method_is_missing()
    {
        var act = () => new GenericDispatcher<Func<GenericDispatcherTests, int, Task<string>>>(
            typeof(GenericDispatcherTests), "Nope", [typeof(int)]);
        act.Should().Throw<InvalidOperationException>().WithMessage("*private instance or static method*");
    }

    [Fact]
    public void Constructor_throws_when_the_method_is_not_generic()
    {
        var act = () => new GenericDispatcher<Func<GenericDispatcherTests, int, Task<string>>>(
            typeof(GenericDispatcherTests), nameof(NotGeneric), [typeof(int)]);
        act.Should().Throw<InvalidOperationException>().WithMessage("*NotGeneric*");
    }
}
