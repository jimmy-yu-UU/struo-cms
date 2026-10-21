using System.Collections;
using System.Reflection;
using SqlSugar;

namespace Struo.Tests.Persistence;

/// <summary>
/// Counts the entries in SqlSugar's process-wide client registries. A <see cref="SqlSugarScope"/>
/// keeps its inner clients in two private static dictionaries, <c>CallContextAsync&lt;SqlSugarClient&gt;.state</c>
/// (an <c>AsyncLocal</c> per scope) and <c>CallContextThread&lt;SqlSugarClient&gt;.state</c> (a
/// <c>ThreadLocal</c> per scope), keyed by the scope instance's identity. Nothing removes an entry, so
/// the key count equals the number of scopes that ever touched the database; reflection is the only
/// way to observe it.
/// </summary>
internal static class SqlSugarStaticContext
{
    public static int AsyncKeys => Count(typeof(CallContextAsync<SqlSugarClient>));

    public static int ThreadKeys => Count(typeof(CallContextThread<SqlSugarClient>));

    private static int Count(Type registry)
    {
        var field = registry.GetField("state", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                $"SqlSugar no longer has a static 'state' field on {registry.Name}; update this helper.");
        return ((ICollection)field.GetValue(null)!).Count;
    }
}

/// <summary>
/// Tests that count SqlSugar's static registries must not overlap with any other test that builds a
/// scope, so this collection never runs in parallel with another one.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlSugarStaticStateCollection
{
    public const string Name = "SqlSugarStaticState";
}
