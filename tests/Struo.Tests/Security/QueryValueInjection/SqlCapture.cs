// tests/Struo.Tests/Security/QueryValueInjection/SqlCapture.cs
using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace Struo.Tests.Security.QueryValueInjection;

/// <summary>Collects every statement the host's SqlSugarScope runs.</summary>
internal sealed class SqlCapture
{
    private readonly ConcurrentQueue<string> _log = new();

    // SqlSugarScope copies its root ConnectionConfig (AopEvents included) into each inner client it creates,
    // so a hook placed on the root config reaches every request flow that starts afterwards.
    public static SqlCapture Attach(IServiceProvider services)
    {
        var db = services.GetRequiredService<ISqlSugarClient>();
        var field = typeof(SqlSugarScope).GetField("_configs", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("SqlSugarScope._configs not found");
        var capture = new SqlCapture();
        foreach (var c in (List<ConnectionConfig>)field.GetValue(db)!)
        {
            c.AopEvents ??= new AopEvents();
            c.AopEvents.OnLogExecuting = (sql, pars) => capture._log.Enqueue(Render(sql, pars));
        }
        return capture;
    }

    public void Clear() => _log.Clear();

    public IReadOnlyList<string> Matching(string needle) =>
        _log.Where(s => s.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();

    public IReadOnlyList<string> All => _log.ToList();

    private static string Render(string sql, SugarParameter[]? pars) =>
        pars is null || pars.Length == 0
            ? sql
            : sql + "\n  -- params: " + string.Join(", ", pars.Select(p => $"{p.ParameterName}={p.Value}"));
}
