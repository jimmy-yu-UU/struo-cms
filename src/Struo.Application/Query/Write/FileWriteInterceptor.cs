// src/Struo.Application/Query/Write/FileWriteInterceptor.cs
using Struo.Application.Files;
using Struo.Application.Metadata;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// The <c>file</c> collection's write rule: a file is either published (readable by anyone with its id)
/// or draft (file writers only), so a <c>status</c> in the body must be one of the two.
/// </summary>
public sealed class FileWriteInterceptor(IEntityRegistry registry) : IItemWriteInterceptor
{
    private const string StatusField = "status";

    public bool AppliesTo(string collection) =>
        string.Equals(collection, FileCollection.Name, StringComparison.OrdinalIgnoreCase);

    public Task BeforeWriteAsync(ItemWriteContext context, CancellationToken ct)
    {
        if (context.Operation is not (ItemWriteOperation.Update or ItemWriteOperation.Revert)
            || !context.BodyKeys.Contains(StatusField)) return Task.CompletedTask;

        var descriptor = registry.Get(context.Meta.Name)!;
        if (!descriptor.FieldToProperty.TryGetValue(StatusField, out var property)) return Task.CompletedTask;

        var status = descriptor.Properties.GetValueOrDefault(property)?.GetValue(context.Entity) as string;
        if (status is not (FileStorageOptions.PublishedStatus or FileStorageOptions.DraftStatus))
            throw new QueryException(
                $"Field 'status' must be '{FileStorageOptions.PublishedStatus}' or '{FileStorageOptions.DraftStatus}'.");
        return Task.CompletedTask;
    }
}
