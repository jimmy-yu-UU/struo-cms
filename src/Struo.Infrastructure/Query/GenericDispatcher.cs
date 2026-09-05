// src/Struo.Infrastructure/Query/GenericDispatcher.cs
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Struo.Infrastructure.Query;

// Caches a per-closed-type delegate over a private generic method (instance or static), so a
// repeated call for the same closed type pays MethodInfo.MakeGenericMethod + CreateDelegate once
// instead of on every request. The dispatched method must be found via NonPublic | Instance |
// Static binding and must be an open generic method definition. `MethodInfo.CreateDelegate(Type)`
// (no target) adapts to whichever kind is resolved: for an instance method the resulting delegate
// is the "open-instance" form — its FIRST parameter is the receiver, remaining parameters plus the
// return type matching the closed method's exact signature; for a static method (one that needs no
// receiver, e.g. it touches no instance state) the delegate matches the closed method's signature
// with no extra leading parameter. Every dispatched method is expected to be async (or to return a
// Task directly), so any exception it throws surfaces on the awaited Task exactly as it would from
// a direct call — there is no TargetInvocationException wrapping to preserve, because no
// MethodInfo.Invoke is ever used to call the closed method.
internal sealed class GenericDispatcher<TDelegate> where TDelegate : Delegate
{
    private readonly MethodInfo definition;
    private readonly ConcurrentDictionary<Type, TDelegate> cache = new();

    public GenericDispatcher(Type host, string methodName, Type[] parameterTypes)
    {
        definition = GenericDispatcherSupport.ResolveDefinition(host, methodName, parameterTypes);
    }

    public TDelegate For(Type entityType) =>
        cache.GetOrAdd(entityType,
            static (t, def) => def.MakeGenericMethod(t).CreateDelegate<TDelegate>(),
            definition);
}

// Same caching scheme as GenericDispatcher, but for a method with two independent type parameters
// (e.g. an entity type plus a foreign-key CLR type), keyed by the closed (first, second) pair.
internal sealed class BiGenericDispatcher<TDelegate> where TDelegate : Delegate
{
    private readonly MethodInfo definition;
    private readonly ConcurrentDictionary<(Type First, Type Second), TDelegate> cache = new();

    public BiGenericDispatcher(Type host, string methodName, Type[] parameterTypes)
    {
        definition = GenericDispatcherSupport.ResolveDefinition(host, methodName, parameterTypes);
    }

    public TDelegate For(Type first, Type second) =>
        cache.GetOrAdd((first, second),
            static (key, def) => def.MakeGenericMethod(key.First, key.Second).CreateDelegate<TDelegate>(),
            definition);
}

internal static class GenericDispatcherSupport
{
    [SuppressMessage("Major Code Smell", "S3011:Reflection should not be used to increase accessibility of classes, methods, or fields",
        Justification = "Binds the host's own private generic workers by nameof; not reachable from external input.")]
    public static MethodInfo ResolveDefinition(Type host, string methodName, Type[] parameterTypes)
    {
        var method = host.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static, parameterTypes)
            ?? throw new InvalidOperationException(
                $"'{host.Name}' has no private instance or static method '{methodName}' with the given parameter types.");
        if (!method.IsGenericMethodDefinition)
            throw new InvalidOperationException(
                $"'{host.Name}.{methodName}' is not a generic method definition.");
        return method;
    }
}
