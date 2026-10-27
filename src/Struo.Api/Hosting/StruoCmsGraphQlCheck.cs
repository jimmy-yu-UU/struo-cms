// src/Struo.Api/Hosting/StruoCmsGraphQlCheck.cs
using HotChocolate;
using HotChocolate.Execution;
using Struo.Domain.Metadata;

namespace Struo.Api.Hosting;

/// <summary>Registered when a fork added to the GraphQL schema through <c>ConfigureGraphQl</c>, so startup builds and checks the schema.</summary>
internal sealed class StruoCmsGraphQlMarker;

/// <summary>
/// Builds the GraphQL schema at startup when a fork extended it, so a name that clashes with the schema
/// the CMS generates fails the host with the collision message instead of the first request.
/// </summary>
internal static class StruoCmsGraphQlCheck
{
    internal static async Task RunAsync(IServiceProvider services)
    {
        if (services.GetService<StruoCmsGraphQlMarker>() is null) return;

        try
        {
            await services.GetRequiredService<IRequestExecutorProvider>().GetExecutorAsync();
        }
        catch (SchemaException ex) when (ex.Errors.All(e => e.Exception is not MetadataException))
        {
            throw new MetadataException(
                "GraphQL name collision: a type or field added through ConfigureGraphQl clashes with the schema. "
                + string.Join(" ", ex.Errors.Select(e => e.Message.Replace('`', (char)39)))
                + " Rename it so the name is unique.");
        }
    }
}
