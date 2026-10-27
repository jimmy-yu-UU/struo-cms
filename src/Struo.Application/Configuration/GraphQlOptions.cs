// src/Struo.Application/Configuration/GraphQlOptions.cs
namespace Struo.Application.Configuration;

public sealed class GraphQlOptions
{
    public const string SectionName = "GraphQl";

    /// <summary>
    /// Whether this instance may disclose its GraphQL schema. Governs BOTH routes that can read it:
    /// introspection queries (<c>__schema</c>/<c>__type</c>) and HotChocolate's built-in
    /// <c>GET /graphql?sdl</c>. They are one concern: gating only introspection while leaving
    /// <c>?sdl</c> open would still serve the complete SDL to an anonymous caller in Production, even
    /// though a reader might reasonably assume introspection-off is enough.
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

    /// <summary>Deepest selection nesting an operation may have; introspection fields are exempt. Must be greater than 0.</summary>
    public int MaxExecutionDepth { get; set; } = 12;

    /// <summary>Highest static field cost one operation may accrue. Must be greater than 0.</summary>
    public double MaxFieldCost { get; set; } = 150;

    /// <summary>Highest static type cost one operation may accrue. Must be greater than 0.</summary>
    public double MaxTypeCost { get; set; } = 150;
}
