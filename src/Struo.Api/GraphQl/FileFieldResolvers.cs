// src/Struo.Api/GraphQl/FileFieldResolvers.cs
using HotChocolate.Resolvers;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;

namespace Struo.Api.GraphQl;

/// <summary>
/// STUB: resolver bodies return null for now — the DataLoader bodies (batched File lookups) are
/// filled in Task 9. This task only needs the field CONFIGURATIONS to exist so the schema builds.
/// </summary>
internal static class FileFieldResolvers
{
    // "<name>Id" (Image/File) -> "<name>": File, resolved by id via DataLoader.
    internal static ObjectFieldConfiguration ScalarFileField(string idFieldName)
    {
        var name = idFieldName.EndsWith("Id", StringComparison.Ordinal) ? idFieldName[..^2] : idFieldName + "File";
        return new ObjectFieldConfiguration(name, null, TypeReference.Parse("File"),
            resolver: ctx => ResolveScalar(ctx, idFieldName));
    }

    // "<name>" (Files) -> "<name>Files": [File!], resolved batched.
    internal static ObjectFieldConfiguration ListFileField(string listFieldName)
        => new(listFieldName + "Files", null, TypeReference.Parse("[File!]"),
            resolver: ctx => ResolveList(ctx, listFieldName));

    private static ValueTask<object?> ResolveScalar(IResolverContext ctx, string idFieldName)
        => ValueTask.FromResult<object?>(null); // filled in Task 9

    private static ValueTask<object?> ResolveList(IResolverContext ctx, string listFieldName)
        => ValueTask.FromResult<object?>(null); // filled in Task 9
}
