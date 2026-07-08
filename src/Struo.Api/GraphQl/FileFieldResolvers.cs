// src/Struo.Api/GraphQl/FileFieldResolvers.cs
using System.Collections;
using GreenDonut;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;

namespace Struo.Api.GraphQl;

/// <summary>
/// Resolves the synthetic <c>heroImage: File</c> / <c>galleryFiles: [File!]</c> fields (Task 7)
/// added next to a scalar Image/File id column or a Files id-list column. Every file id requested
/// across ALL sibling nodes in one GraphQL request is batched into a SINGLE
/// <see cref="IGraphQlDataSource.QueryAsync"/>("file", id _in keys) call via <see cref="FileByIdDataLoader"/>
/// — the N+1 regression this task closes. A missing/deleted id resolves to null.
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

    private static async ValueTask<object?> ResolveScalar(IResolverContext ctx, string idFieldName)
    {
        var raw = ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault(idFieldName);
        var id = ToGuid(raw);
        if (id is null) return null;
        return await ctx.DataLoader<FileByIdDataLoader>().LoadAsync(id.Value, ctx.RequestAborted);
    }

    private static async ValueTask<object?> ResolveList(IResolverContext ctx, string listFieldName)
    {
        var raw = ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault(listFieldName);
        if (raw is not IEnumerable seq) return Array.Empty<object>();

        var ids = seq.Cast<object?>().Select(ToGuid).Where(g => g is not null).Select(g => g!.Value).ToArray();
        if (ids.Length == 0) return Array.Empty<object>();

        var loaded = await ctx.DataLoader<FileByIdDataLoader>().LoadAsync(ids, ctx.RequestAborted);
        return loaded.Where(x => x is not null).Cast<object>().ToList(); // preserve order, drop missing
    }

    private static Guid? ToGuid(object? v) => v switch
    {
        Guid g => g,
        string s when Guid.TryParse(s, out var g) => g,
        _ => null,
    };

    /// <summary>
    /// Manual GreenDonut <see cref="BatchDataLoader{TKey,TValue}"/> — constructed ad-hoc per request
    /// by HotChocolate (<c>ctx.DataLoader&lt;T&gt;()</c>); no DI registration is required for a
    /// manually-written subclass (verified against the installed HotChocolate/GreenDonut 16.4.0 —
    /// the inline registration-free <c>ctx.BatchDataLoader&lt;TKey,TValue&gt;(fetch)</c> helper from
    /// v13-15 was removed in v15; v16's replacement is this class shape + <c>ctx.DataLoader&lt;T&gt;()</c>).
    /// One batched <see cref="IGraphQlDataSource.QueryAsync"/>("file", id _in keys) call per request,
    /// keyed by id; ids absent from the result are simply omitted from the batch's returned
    /// dictionary, which GreenDonut surfaces as a null <c>LoadAsync</c> result (no throw).
    /// </summary>
    internal sealed class FileByIdDataLoader(
        IGraphQlDataSource dataSource,
        IBatchScheduler batchScheduler,
        DataLoaderOptions options)
        : BatchDataLoader<Guid, IReadOnlyDictionary<string, object?>>(batchScheduler, options)
    {
        protected override async Task<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, object?>>> LoadBatchAsync(
            IReadOnlyList<Guid> keys, CancellationToken cancellationToken)
        {
            var filter = new Dictionary<string, object?>
            {
                ["id"] = new Dictionary<string, object?> { ["in"] = keys.Cast<object?>().ToList() },
            };
            var query = GraphQlQueryBuilder.BuildQuery(filter, null, keys.Count, 0, null, Array.Empty<string>());
            var page = await dataSource.QueryAsync("file", query, null, cancellationToken);

            var map = new Dictionary<Guid, IReadOnlyDictionary<string, object?>>();
            foreach (var row in page.Data)
                if (ToGuid(row.GetValueOrDefault("id")) is { } g) map[g] = row;
            return map;
        }
    }
}
