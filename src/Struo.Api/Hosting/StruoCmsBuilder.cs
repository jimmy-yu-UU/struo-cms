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

    /// <summary>
    /// Adds to the GraphQL schema. <paramref name="configure"/> receives HotChocolate's own builder, after
    /// the core schema is registered and before the server is built, so a fork can add type extensions on
    /// <c>Query</c> and <c>Mutation</c>, object types, scalars and middleware. Types and root fields whose
    /// names clash with the schema the CMS generates fail startup.
    /// </summary>
    public StruoCmsBuilder ConfigureGraphQl(Action<IRequestExecutorBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _graphQlActions.Add(configure);
        return this;
    }

    internal bool HasGraphQlConfiguration => _graphQlActions.Count > 0;

    internal void ApplyGraphQl(IRequestExecutorBuilder graphQl)
    {
        foreach (var action in _graphQlActions) action(graphQl);
    }
}
