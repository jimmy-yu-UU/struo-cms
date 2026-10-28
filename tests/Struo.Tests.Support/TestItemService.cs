// tests/Struo.Tests/Support/TestItemService.cs
using Struo.Application.Abstractions;
using Struo.Application.Changes;
using Struo.Application.Configuration;
using Struo.Application.Localization;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Application.Revisions;
using Struo.Application.Search;
using Struo.Application.Security;
using Struo.Infrastructure.Security;

namespace Struo.Tests.Support;

/// <summary>
/// Builds an <see cref="ItemService"/> the way DI does, for the unit harnesses that wire it by hand.
/// The arguments the harnesses differ on are required; the rest default to what the harnesses used: all
/// permissions allowed, default query options, the Ganss sanitizer, and no search
/// provider, notifier, interceptors or delete handlers.
/// </summary>
public static class TestItemService
{
    public static ItemService Create(
        IItemRepository repository,
        IMetadataProvider metadata,
        IEntityRegistry registry,
        IRelationshipGraph graph,
        IRelationExpander expander,
        IM2MDescriptorSource m2mSource,
        ILanguageProvider languages,
        ICurrentUserAccessor currentUser,
        IRevisionStore revisions,
        RevisionSnapshotBuilder snapshotBuilder,
        IPermissionService? permissions = null,
        StruoQueryOptions? options = null,
        ISearchProvider? searchProvider = null,
        IItemChangeNotifier? notifier = null,
        IEnumerable<IItemWriteInterceptor>? interceptors = null,
        IEnumerable<ICollectionDeleteHandler>? deleteHandlers = null) =>
        new(repository, metadata, registry, permissions ?? new AllowAllPermissionService(), graph, expander,
            m2mSource, languages, options ?? new StruoQueryOptions(), new GanssHtmlSanitizer(), currentUser,
            revisions, snapshotBuilder, searchProvider, notifier, interceptors, deleteHandlers);
}
