// src/Struo.Api/Hosting/StruoCmsGraphQlCheck.cs
using HotChocolate;
using HotChocolate.Execution;
using Struo.Domain.Metadata;

namespace Struo.Api.Hosting;

/// <summary>Registered when a fork added to the GraphQL schema through <c>ConfigureGraphQl</c>, so startup builds and checks the schema.</summary>
internal sealed record StruoCmsGraphQlCheckState(bool ForkExtended);

/// <summary>
/// Builds the GraphQL schema at startup when a fork extended it, so a name that clashes with the schema
/// the CMS generates fails the host with the collision message instead of the first request. Any other
/// schema error propagates unchanged.
/// </summary>
internal static class StruoCmsGraphQlCheck
{
    private const string DuplicateTypeMessage = "was already registered by another type";

    internal static async Task RunAsync(IServiceProvider services)
    {
        if (services.GetService<StruoCmsGraphQlCheckState>() is not { ForkExtended: true }) return;

        try
        {
            await services.GetRequiredService<IRequestExecutorProvider>().GetExecutorAsync();
        }
        catch (SchemaException ex) when (ex.Errors.Any(IsDuplicateName))
        {
            throw new MetadataException(
                "GraphQL name collision: a type added through ConfigureGraphQl clashes with a name the schema already has. "
                + string.Join(" ", ex.Errors.Where(IsDuplicateName).Select(e => e.Message.Replace('`', (char)39)))
                + " Rename it so the name is unique.",
                ex);
        }
    }

    private static bool IsDuplicateName(ISchemaError error) =>
        error.Message.Contains(DuplicateTypeMessage, StringComparison.Ordinal);
}
