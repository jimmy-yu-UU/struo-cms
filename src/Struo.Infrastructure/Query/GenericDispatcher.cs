// src/Struo.Infrastructure/Query/GenericDispatcher.cs
using System.Collections.Concurrent;
using System.Reflection;

namespace Struo.Infrastructure.Query;

// Caches a per-closed-type open-instance delegate over a private generic instance method, so a
// repeated call for the same closed type pays MethodInfo.MakeGenericMethod + CreateDelegate once
// instead of on every request. The dispatched method must be found via NonPublic | Instance
// binding and must be an open generic method definition; the resulting delegate's FIRST parameter
// is the receiver (the "open-instance" delegate form), and the remaining parameters plus the
// return type must match the closed method's exact signature. Every dispatched method is expected
// to be async (or to return a Task directly), so any exception it throws surfaces on the awaited
// Task exactly as it would from a direct call — there is no TargetInvocationException wrapping to
// preserve, because no MethodInfo.Invoke is ever used to call the closed method.
internal sealed class GenericDispatcher<TDelegate> where TDelegate : Delegate
{
    private readonly MethodInfo definition;
    private readonly ConcurrentDictionary<Type, TDelegate> cache = new();

    public GenericDispatcher(Type host, string methodName, Type[] parameterTypes)
    {
        definition = GenericDispatcherSupport.ResolveDefinition(host, methodName, parameterTypes);
    }

    public TDelegate For(Type entityType) =>
        cache.GetOrAdd(entityType, t => definition.MakeGenericMethod(t).CreateDelegate<TDelegate>());
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
            key => definition.MakeGenericMethod(key.First, key.Second).CreateDelegate<TDelegate>());
}

internal static class GenericDispatcherSupport
{
    public static MethodInfo ResolveDefinition(Type host, string methodName, Type[] parameterTypes)
    {
        var method = host.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance, parameterTypes)
            ?? throw new InvalidOperationException(
                $"'{host.Name}' has no private instance method '{methodName}' with the given parameter types.");
        if (!method.IsGenericMethodDefinition)
            throw new InvalidOperationException(
                $"'{host.Name}.{methodName}' is not a generic method definition.");
        return method;
    }
}
