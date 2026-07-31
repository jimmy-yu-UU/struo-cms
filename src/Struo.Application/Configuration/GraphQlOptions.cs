// src/Struo.Application/Configuration/GraphQlOptions.cs
namespace Struo.Application.Configuration;

public sealed class GraphQlOptions
{
    public const string SectionName = "GraphQl";

    /// <summary>
    /// Whether this instance may disclose its GraphQL schema. Governs BOTH routes that can read it:
    /// introspection queries (<c>__schema</c>/<c>__type</c>) and HotChocolate's built-in
    /// <c>GET /graphql?sdl</c>. They are one concern and were previously gated inconsistently — only
    /// introspection was closed outside Development, so <c>?sdl</c> served the complete SDL to an
    /// anonymous caller in Production while a reader of the code would reasonably conclude otherwise.
    /// <para>
    /// <c>null</c> (the default) means "Development only", which preserves the shipped behavior. Set it
    /// explicitly to override per environment without a rebuild — <c>GraphQl__ExposeSchema=true</c> is
    /// the intended switch for a fork that deliberately publishes a public GraphQL API and wants its
    /// schema readable in Production.
    /// </para>
    /// <para>
    /// This is schema DISCLOSURE only. Query execution through <c>POST /graphql</c> is unaffected in
    /// either setting: a client that already knows its queries never reads the schema at runtime, so
    /// closing this does not break any GraphQL consumer. What it does affect is schema-dependent
    /// TOOLING — codegen, Postman/Insomnia schema import, Apollo Sandbox — which should point at a
    /// Development or staging instance. The Nitro browser IDE is gated separately (and stays
    /// Development-only regardless of this setting): shipping a browser IDE is a much larger decision
    /// than exposing SDL text.
    /// </para>
    /// </summary>
    public bool? ExposeSchema { get; set; }
}
