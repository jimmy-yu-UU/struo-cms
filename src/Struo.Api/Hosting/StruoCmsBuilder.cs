// src/Struo.Api/Hosting/StruoCmsBuilder.cs
using HotChocolate.Execution.Configuration;

namespace Struo.Api.Hosting;

/// <summary>
/// The hook object handed to the optional callback of
/// <see cref="StruoCmsHostingExtensions.AddStruoCms"/>. It is the place where a fork reaches into core
/// registration.
/// </summary>
public sealed class StruoCmsBuilder
{
    private readonly List<Action<IRequestExecutorBuilder>> _graphQlActions = [];

    private bool _graphQlApplied;

    /// <summary>
    /// Adds to the GraphQL schema. <paramref name="configure"/> receives HotChocolate's own builder, after
    /// the core schema is registered and before the server is built, so a fork can add type extensions on
    /// <c>Query</c> and <c>Mutation</c>, object types, scalars and middleware. Types and root fields whose
    /// names clash with the schema the CMS generates fail startup.
    /// <para>
    /// This exposes HotChocolate's API directly. The actions run after the core settings, so a setting
    /// changed on the builder (introspection, cost and depth rules, dependency-injection scopes) overrides
    /// the core's. A field a fork adds to <c>Mutation</c> carries none of the CMS's sign-in check or
    /// permissions, which are attached to the generated fields only; the fork authorizes its own fields.
    /// </para>
    /// <para>Call it from the <c>AddStruoCms</c> callback; a call after <c>AddStruoCms</c> returned throws.</para>
    /// </summary>
    public StruoCmsBuilder ConfigureGraphQl(Action<IRequestExecutorBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (_graphQlApplied)
            throw new InvalidOperationException(
                "ConfigureGraphQl must be called from the AddStruoCms callback; AddStruoCms has already applied the GraphQL configuration.");
        _graphQlActions.Add(configure);
        return this;
    }

    internal bool HasGraphQlConfiguration => _graphQlActions.Count > 0;

    internal void ApplyGraphQl(IRequestExecutorBuilder graphQl)
    {
        _graphQlApplied = true;
        foreach (var action in _graphQlActions) action(graphQl);
    }
}
