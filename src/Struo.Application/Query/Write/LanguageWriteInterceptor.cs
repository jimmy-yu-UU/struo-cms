// src/Struo.Application/Query/Write/LanguageWriteInterceptor.cs
using Struo.Application.Localization;

namespace Struo.Application.Query;

/// <summary>
/// The <c>language</c> collection's write rules: its invariants are checked inside the write transaction
/// (a violation rolls it back and answers 400), and the request's language cache is dropped once the
/// write is committed.
/// </summary>
public sealed class LanguageWriteInterceptor(ILanguageCollectionRules rules, ILanguageProvider languages)
    : IItemWriteInterceptor
{
    private const string CollectionName = "language";

    public bool AppliesTo(string collection) =>
        string.Equals(collection, CollectionName, StringComparison.OrdinalIgnoreCase);

    // Trash and restore do not change the invariants' inputs, so only the other operations check them.
    public Task InTransactionAsync(ItemWriteContext context, CancellationToken ct) =>
        context.Operation is ItemWriteOperation.Trash or ItemWriteOperation.Restore
            ? Task.CompletedTask
            : rules.EnsureInvariantsAsync(ct);

    public Task AfterCommitAsync(ItemWriteContext context, CancellationToken ct)
    {
        if (context.Changed) languages.Invalidate();
        return Task.CompletedTask;
    }
}
