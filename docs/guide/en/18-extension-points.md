# 18. Extension Points

How a caller's `search=` gets answered, what happens after a write, and what the GraphQL schema
contains are each left by the core to a seam of their own: a fork plugs in its own implementation
without touching the core's query or write path at all. This chapter covers where a fork registers
its services, what each of `ISearchProvider` and `IItemChangeListener` promises, when each is called,
what happens on failure, how write interceptors and delete handlers add business rules to item writes, how to bring your own file storage, how to define your own client-facing errors
with `StruoDomainException`, and how `ConfigureGraphQl` adds to the GraphQL schema.

`search=`'s own syntax and the built-in `LIKE` scan are in
[Chapter 10: Querying: Filters, Sorting and Pagination](10-query-basics.md); a write's revision
history and soft-delete semantics are in
[Chapter 9: Revisions and Soft Delete](09-revisions-and-trash.md).

## Where the two interfaces fit in the architecture

`ISearchProvider` and `IItemChangeListener` are two of the interfaces the core hands out, listed
in [Chapter 2: Architecture](02-architecture.md) under "What can be swapped, what can't" — sitting
at the same layer as `IFileStorage`.

The core provides only a `NullSearchProvider` that never handles a search, and an
`ItemChangeNotifier` that dispatches notifications but has no listener registered by default. A
fork that wants to take over either seam just implements the interface and registers it — none of
the core's own code needs to change.

## Where your registrations go

Your fork's `Program.cs` is yours. The core composes itself in two calls: `builder.AddStruoCms()`
registers every service, and `app.UseStruoCmsAsync()` builds the request pipeline and runs the
startup checks. Register your own services between them, after `AddStruoCms()`. This is an excerpt
of the shipped `src/Struo.Api/Program.cs`, shortened to the lines that matter here; the full file
also sets up the bootstrap logger and the startup error handling:

```csharp
var cli = MigrationCli.Parse(args);
var builder = WebApplication.CreateBuilder(cli.HostArgs);

builder.AddStruoCms();
builder.Services.AddScoped<ISearchProvider, MySearchProvider>();
builder.Services.AddScoped<IItemChangeListener, MyIndexer>();

var app = builder.Build();
if (await app.TryRunStruoCommandAsync(cli)) return;
await app.UseStruoCmsAsync();
await app.RunAsync();
```

`AddStruoCms()` reads configuration while it runs, to scan `Struo:ContentAssemblies`. Add any
configuration source of your own (Key Vault, for example) to `builder.Configuration` before the
call, or the scan won't see it.

A registration made after `AddStruoCms()` always wins over the core's default. The core registers
these defaults with `TryAdd`, so a registration made before the call is kept too: `ISearchProvider`,
`IItemChangeNotifier`, `IHtmlSanitizer`, `IPasswordHasher`, `IFileAccessPolicy` and `IFileStorage`.

Three of those carry a contract a replacement has to keep:

- `IPasswordHasher` must verify the Argon2id hashes already stored, and compare in constant time.
- `IHtmlSanitizer` is the only defence against script injection in rich-text fields.
- `IFileAccessPolicy` is the permission check on file downloads.

## The search provider: `ISearchProvider`

`ISearchProvider` is the search-side extension point: a fork wires in Meilisearch,
Elasticsearch, PostgreSQL full-text search, or any other engine, and `search=` gets answered by
that engine instead. What comes back to the core is a clean set of candidate ids — the core never
needs to know which kind of index sits behind it.

### The contract

`ISearchProvider` has exactly one method:
`Task<SearchOutcome> SearchAsync(SearchRequest request, CancellationToken ct = default)`.

`SearchRequest` carries these members:

- `Collection` — the collection's canonical camelCase name, not however the caller happened to
  spell it in the route. REST route matching is case-insensitive, so the provider always sees the
  same spelling.
- `Term` — the raw value of `search=`, neither trimmed nor lower-cased.
- `Locale` — the locale this particular query actually resolves to: an explicit `locale=` if one
  was supplied, the site default otherwise, computed even for a collection with no translations.
- `SearchableFields` — the list of `Searchable`, non-`Hidden` fields the core computes on its own.
  It's only a hint; a provider is free to ignore it and search whatever fields it indexes itself.

The answer is one of exactly two states: `SearchOutcome.NotHandled`, or a result built with
`SearchOutcome.Candidates(ids)`. Reading it back exposes `Handled` (a boolean) and `Ids` (a
nullable string list).

`NotHandled` means the core's built-in `LIKE` scan runs as usual, as if no provider had been asked
at all. `Candidates(ids)` hands back a batch of root ids as strings, and those replace the `LIKE`
search outright — `search=`'s own term is never matched against anything after that.

An empty candidate list is a search that was handled with zero hits, not a fallback to `LIKE`: a
provider that genuinely found nothing should still return `Candidates([])`, never `NotHandled`.
Passing `null` to `Candidates` throws immediately — the two states deliberately don't share one
nullable parameter. The core's built-in `NullSearchProvider` always returns `NotHandled`, so
without a fork's own provider registered, `search=` runs the built-in `LIKE` scan.

### When the provider is asked

The core only asks the provider on a list request, and only when `search=` is non-blank — once
per request. A single-item `GET` never asks it, and neither does relation expansion.

### How it combines with other conditions

The candidate set the provider returns is ANDed with `filter`: a row has to satisfy `filter` and
fall inside the candidate set to count. The candidate set is also bound by whatever `deleted=`
mode is in effect — a row sitting in the trash can't be smuggled back in through the candidate
set. The candidate set's own order has no effect on result order; `sort=` (or the default order
when `sort=` is absent) is what decides it.

Permissions aren't a per-row concern here: the collection-level read check already ran before the
provider is ever asked, so a candidate id carries no per-row authorization of its own; `Hidden` is
a field-level concern and has nothing to do with which rows are eligible for the candidate set. The
list itself, every facet, and the aggregate all share the one resolved candidate set — see
[Chapter 11: Querying: Projection, Deep Expansion, Facets and Aggregates](11-query-advanced.md).

### Ids are a trust boundary

Every id the provider returns ends up as part of a SQL `id IN (…)` condition, built from typed SQL
literals rather than a parameterized query — which makes it a trust boundary, not user input
already validated on the query string. Each id is first parsed into the collection's primary-key
CLR type; only `Guid` or an integer primary key (`long`, `int`, `short`) is supported, and every
other key type is rejected outright.

The candidate count is capped by `Query:MaxSearchCandidates` — the key is in
[Chapter 4: Configuration Reference](04-configuration.md).

An id that can't be parsed, an unsupported key type, or a count over the cap are all the
provider's own contract violations, and they always come back as `500` `INTERNAL_SERVER_ERROR`,
never `400` — the mistake belongs to the provider, not the caller. Every one of these `500`s is
logged at Error level server-side, so an operator can see the problem even though the caller only
ever sees the masked generic message.

Duplicate ids are silently deduplicated. Candidate ids never pass through the query validator at
all — they're the provider's output, not parsed user input — and the validator, as a further
layer of defense, clears out any candidate field that arrived from outside before it starts
processing a request.

### When the engine is down

When the provider's own engine can't answer at all — unreachable, timed out, index missing — it
should throw `SearchUnavailableException`: the request fails, REST returns `503`
`SEARCH_UNAVAILABLE` (see [Chapter 12: REST API Conventions](12-rest-conventions.md)), and GraphQL
reports the same code while keeping the transport-layer status at `200` (see
[Chapter 14: GraphQL API](14-graphql.md)).

This exception's own message may carry an internal hostname, so the caller only ever sees the
fixed, generic message; the real reason is written only to the server-side log. A provider that
would rather degrade gracefully can catch the exception itself and return `NotHandled`, falling
back to the built-in `LIKE` scan — both paths are legitimate, and the interface doesn't force
either one.

### Registration and lifetime

The core registers its own default with `TryAdd` — only if nothing is registered yet — so a
fork's registration always wins, whether it runs before or after `AddStruoCms()`: before wins
because the core's default then never takes effect; after wins because whatever registers last is
what actually gets resolved.

Both `ISearchProvider` and the next section's `IItemChangeListener` only need to be registered in
an assembly the API host project (`Struo.Api`) references — no need to list that assembly in
`Struo:ContentAssemblies`, since that list only scans for `[CmsCollection]` types and isn't a
registry for extension points.

Use a scoped or transient lifetime, never singleton, unless the provider is genuinely stateless: a
singleton that captures a scoped dependency in its constructor either gets stopped at startup by
scope validation, or ends up as one instance reused for the whole application lifetime instead of
one per request. Exactly one provider is ever resolved at this seam.

Keeping the index itself up to date as data changes isn't this interface's job — the interface
that tells a fork "this row changed" is `IItemChangeListener`, covered next.

### A minimal implementation

Below is a minimal implementation; `IMyIndex` stands in for a fork's own engine client and isn't a
type the framework includes.

```csharp
using Struo.Application.Search;

public sealed class MySearchProvider(IMyIndex index) : ISearchProvider
{
    public async Task<SearchOutcome> SearchAsync(SearchRequest request, CancellationToken ct = default)
    {
        if (request.Collection != "article") return SearchOutcome.NotHandled;

        IReadOnlyList<string> ids = await index.LookupAsync(request.Term, request.Locale, ct);
        return SearchOutcome.Candidates(ids);   // an empty list is still a handled, zero-hit search
    }
}
```

The collection name and the index lookup are the two things a fork swaps out: every other
collection just returns `NotHandled`, and `search=` keeps running the built-in `LIKE` scan for it.

Register it in your `Program.cs`, after `AddStruoCms()`:

```csharp
builder.Services.AddScoped<ISearchProvider, MySearchProvider>();
```

## Change notifications: `IItemChangeListener`

`IItemChangeListener` is the write-side extension point. Once data is created, updated, trashed,
restored, or purged, a fork can hook this interface to do its own work — sync a search index, fire
a webhook, clear a cache — with the core's own write path never needing to know what's downstream.

### The contract

`IItemChangeListener` has exactly one method:
`Task OnChangedAsync(IReadOnlyList<ItemChange> changes, CancellationToken ct = default)`.

An `ItemChange`'s members are `Collection`, `Id`, and `Kind`. `Kind`'s values and when each one
fires are in the table under "Which writes are covered".

### Timing and failure semantics

A listener is only called after the transaction has actually committed, so reading the same row
back always shows the state that was really written, never an intermediate value that could still
roll back.

The core's built-in dispatcher, `ItemChangeNotifier`, calls every listener in registration order,
each wrapped in its own try/catch: a listener that throws gets logged at Error level, carrying its
own type name, the number of changes in this batch, and a per-kind breakdown; the next listener
still runs, and the dispatcher itself never rethrows or retries.

A fork that needs to survive a crash has to add its own durable queue inside its listener — that's
not something this interface provides.

Dispatch always uses `CancellationToken.None`, not this request's own token, because the write has
already committed and the caller disconnecting midway shouldn't skip the notification along with
it.

Dispatch is awaited synchronously, before the response goes out: a slow listener makes this
write's own response slow along with it. That's exactly why a genuinely slow downstream belongs
in a queue inside the listener rather than being called inline — the reason is latency, not
durability.

One write, however many rows it touches, produces exactly one call carrying every change that write
involved. A listener finishing cleanly doesn't guarantee the caller ends up seeing success —
deleting or purging a `user` also revokes every session that user holds, and that step can still
fail the whole request; a restore instead re-reads that row after notifying listeners, to build the
response.

### Which writes are covered

| Kind | When it fires |
|---|---|
| `Created` | An item is created |
| `Updated` | An item is updated, or a revision reverted (another update) |
| `Trashed` | Trashed, with at least one row actually affected |
| `Restored` | Restored, with at least one row actually affected |
| `Purged` | The purge target, plus every row reached by cascading delete |

Trashing or restoring a target that's already in that state — trashing an already-trashed item,
restoring an already-live one — is zero rows affected at the SQL level, and no extra notification
fires.

Purging can also touch rows beyond the target itself: any existing row that points at it through a
foreign key set to `null`, and any existing parent row that loses a many-to-many relation because
its junction row was cleared, each get their own `Updated` — clearing a tag counts every existing
article it was attached to, even when the article row itself was never rewritten.

A child row already in the trash whose foreign key would also be set to `null` is skipped
entirely: it was never in any index to begin with, and if it's later restored it gets its own
`Restored`.

The more rows a purge touches, the larger that notification batch gets, and if the same row is
touched more than once in one purge — say its foreign key is first set to `null`, then the row is
actually purged — it appears only once, always as `Purged`.

`Collection` is the collection's canonical name, the same as in a search request; `Id` is the
primary key as a string, always lower-case for a `Guid`.

An ordinary collection's write, whether through REST or GraphQL, ends up on the same core logic,
so both protocols produce identical notifications; a file's own four write paths — upload, trash,
restore, purge — each map onto one of `Created`, `Trashed`, `Restored`, `Purged` too; see
[Chapter 15: Files, Media and Image Transforms](15-files-and-media.md).

### What isn't covered

Account, credential, token, password and login endpoints, plus site settings, are written directly
by their own dedicated stores, bypassing the ordinary collection write path entirely, so none of
them trigger this interface; for a many-to-many relation on an ordinary collection write, only the
side that was actually changed counts — the other side doesn't get a separate notification of its
own.

A write made through any channel outside this API — a fork's own ETL, or a direct database
connection — likewise never passes through here.

The identity collections `user`, `role`, `permission`, and `userRole`, when written through the
generic item API instead (which suits a super-admin), go through the same path as any other
collection and are notified the same way — what differs is which API did the writing, not the
collections themselves.

### Registration, cost, and recursion

The dispatcher itself is always present and needs no registration; a listener does need one — a
fork can register any number of them. They resolve as a collection, unlike the search provider's
single seam, and registration order only decides the order they're called in, not which ones get
called.

Register with scoped; a fork that insists on singleton must keep scoped dependencies out of the
constructor, or it hits the same startup block — or ends up with one instance reused for the
whole application lifetime.

With no listener registered at all, create, update, trash, and restore do no more work than a
bare write — the dispatcher sees an empty list and returns immediately.

Purge is the one exception: with or without a listener, a purge still has to work out which
existing related rows it touches first — one extra typed read per inbound set-null relation, two
per inbound many-to-many junction — and that extra cost belongs to the purge itself, not
something run only for the listener's sake.

This interface only sends the notification; it doesn't confirm the downstream actually took it —
the core never retries a failed call, and a fork whose downstream can go down needs its own
health checks and reconciliation.

If a listener itself writes something else through the ordinary write path — an audit-log entry,
say — that write triggers its own new round of notifications: there's no recursion guard here,
and a listener has to be designed to avoid a write pattern that loops, rather than assuming the
dispatcher will stop it.

### A minimal implementation

The implementation below treats `Trashed` the same as `Purged`:

```csharp
using Struo.Application.Changes;

public sealed class MyChangeListener(IMyIndex index) : IItemChangeListener
{
    public async Task OnChangedAsync(IReadOnlyList<ItemChange> changes, CancellationToken ct = default)
    {
        foreach (var change in changes)
        {
            // A trashed row is filtered out of every read; a stale index entry would show a row
            // the API no longer returns, so treat Trashed like Purged.
            if (change.Kind is ItemChangeKind.Purged or ItemChangeKind.Trashed)
                await index.DeleteAsync(change.Collection, change.Id, ct);
            else
                await index.UpsertAsync(change.Collection, change.Id, ct);
        }
    }
}
```

Swap the two `IMyIndex` calls for whatever downstream action applies; `change.Collection` and
`change.Id` name the row this write touched.

An application can register any number of listeners:

```csharp
builder.Services.AddScoped<IItemChangeListener, MyChangeListener>();
```

## Write interceptors: `IItemWriteInterceptor`

`IItemWriteInterceptor` is the extension point for business rules on an item write: generate a slug,
check one field against another, write an audit row, refuse writes to an append-only collection. It
runs inside the generic write path, so REST and GraphQL both get it, and you do not edit
`ItemService`. The core's own rules for `file`, `user` and `language` are registered the same way.

### The contract

An interceptor has `bool AppliesTo(string collection)` and three hooks. `AppliesTo` receives the
canonical collection name, the same spelling `ISearchProvider` sees. Every hook defaults to a no-op,
so you override only the ones you need.

| Hook | Runs | A throw |
|---|---|---|
| `BeforeWriteAsync` | Outside the transaction, after the merge | Refuses the write |
| `InTransactionAsync` | Inside the transaction, after the row is written | Rolls everything back |
| `AfterCommitAsync` | After the commit, before listeners are notified | Surfaces after the commit |

The merge is the request body applied to the entity. `InTransactionAsync` runs after the row, the many-to-many links and the translations are written, and before the revision is captured. All three hooks take an `ItemWriteContext`.
Its members:

- `Operation`: `Create`, `Update`, `Revert`, `Trash`, `Restore` or `Purge`. A revert is an update that
  re-applies a past snapshot, flagged `Revert`.
- `Meta`: the collection's metadata.
- `Entity`: the entity being written. For create, update and revert it is the request body merged into
  the entity, and you may change it in `BeforeWriteAsync`. For trash, restore and purge it is the
  existing row.
- `Id`: the item id as a string. It is `null` in a create's `BeforeWriteAsync`, because the row does not
  exist yet.
- `Existing`: for update and revert, the row as stored before the merge, so a hook can compare old and
  new. It is loaded only when an interceptor applies to the collection, and it is `null` for the other
  operations.
- `BodyKeys` and `Body`: the top-level property names of the request body, and the body itself.
  `BodyKeys` is empty and `Body` is `null` for trash, restore and purge.
- `Changed`: false in `AfterCommitAsync` when a trash hit an already-trashed row or a restore hit a
  live one.

Trash, restore and purge run `BeforeWriteAsync` with the existing row and no body, and an unknown id
never reaches an interceptor. Their `InTransactionAsync` runs only when the row was actually trashed,
restored or purged. Their `AfterCommitAsync` runs whenever the row exists, so check `Changed` if you
only care about real changes.

### Order and failure

Interceptors that apply to the collection run in registration order, the core's first. Within one
write the hooks run in this order: every interceptor's `BeforeWriteAsync`, the write transaction with
every `InTransactionAsync` inside it, every `AfterCommitAsync`, then the `IItemChangeListener`
notification. On the delete-handler path below, the handler notifies the listeners itself, so
`AfterCommitAsync` follows that notification.

A `StruoDomainException` or a `QueryException` thrown from `BeforeWriteAsync` or
`InTransactionAsync` answers with the 4xx it carries, the same as a thrown validation error. The
core's own exceptions map as they do anywhere else: a `ConflictException` is a `409`, a
`PermissionDeniedException` a `401` or `403`, a `QueryException` a `400`. An exception the API does not
map is a `500`. A throw in `InTransactionAsync` rolls the write back.

`AfterCommitAsync` is different, because the write is committed by then. If one interceptor throws,
the remaining `AfterCommitAsync` hooks are skipped, the listeners are still notified, and then the
exception propagates to the caller. The caller sees an error although the data was saved. Its
`CancellationToken` is not tied to the request, so a disconnecting client does not cut it short. Use
it for cache invalidation and similar work, and keep it from throwing.

### Where it sits in the validation order

On create, the body is checked in this order: permission, the `Creatable` check, body shape,
`MaxLength` and per-field validators, then your `BeforeWriteAsync`, then the Required check. A scalar
Required field is checked after your hook, so a hook can fill it; that is how the slug example below
works. A list-type Required field is checked before it, and an interceptor cannot fill one.

A value your hook sets is not re-checked for `MaxLength` or by field validators, so the hook must
produce a valid value; only the scalar Required check sees it.

### What does not run interceptors

- The dedicated endpoints, such as `POST /api/files` and `POST /api/users`, do not go through
  `ItemService` and run no interceptors. The core's `file` and `user` rules apply only where the
  generic path is used.
- The children of a cascaded purge are removed without running interceptors. They still reach
  `IItemChangeListener`, which is the place to react to them.

### A slug example

This interceptor fills the slug of a new `article` from its title. `Article` is your own collection
class, with a `Title` and a settable `Slug`.

```csharp
using System.Text.RegularExpressions;
using Struo.Application.Query;

public sealed class ArticleSlugInterceptor : IItemWriteInterceptor
{
    public bool AppliesTo(string collection) => collection == "article";

    public Task BeforeWriteAsync(ItemWriteContext context, CancellationToken ct)
    {
        if (context.Operation != ItemWriteOperation.Create) return Task.CompletedTask;

        var article = (Article)context.Entity;
        if (string.IsNullOrWhiteSpace(article.Slug))
            article.Slug = Regex.Replace(article.Title.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return Task.CompletedTask;
    }
}
```

Register it in your own `Program.cs`, after `AddStruoCms()`. Registrations are additive: any number of
interceptors can apply to the same collection.

```csharp
builder.AddStruoCms();
builder.Services.AddScoped<IItemWriteInterceptor, ArticleSlugInterceptor>();
```

## Delete handlers: `ICollectionDeleteHandler`

An interceptor adds to a delete; it cannot replace one. A collection whose delete has side effects the
generic pipeline cannot express, such as a blob to remove, gets an `ICollectionDeleteHandler` instead.
The core's `file` collection uses one.

It has `AppliesTo(string collection)`, `TrashAsync(string id, CancellationToken ct)` and
`PurgeAsync(string id, CancellationToken ct)`, each returning true when the row existed. An optional
`IsValidId(string id)` returns false for an id that cannot name a row, so the delete answers not found
without reading it. The first registered handler that applies wins, and the core's `file` handler is
registered inside `AddStruoCms()`, so a handler of yours for `file` never runs.

`ItemService` checks permissions and that the row exists, runs the interceptors' `BeforeWriteAsync`,
then calls the handler. From there the handler owns the transaction and the change notification, so no
`InTransactionAsync` runs. When the handler returns true, `AfterCommitAsync` runs with `Changed` true.
A handler cannot report that a trash did nothing.

The handler replaces the whole trash or purge, so it must remove the row itself, inside its own
transaction, and notify the change listeners; `ItemService` does neither afterwards.

```csharp
public sealed class AttachmentDeleteHandler(
    IItemRepository repository, IItemChangeNotifier notifier, IMyBlobs blobs) : ICollectionDeleteHandler
{
    public bool AppliesTo(string collection) => collection == "attachment";

    public bool IsValidId(string id) => Guid.TryParse(id, out _);

    public async Task<bool> TrashAsync(string id, CancellationToken ct) => await PurgeAsync(id, ct);

    public async Task<bool> PurgeAsync(string id, CancellationToken ct)
    {
        var removed = await repository.InTransactionAsync(
            () => repository.DeleteAsync("attachment", id, ct), ct);
        if (!removed) return false;

        await notifier.NotifyAsync([new ItemChange("attachment", id, ItemChangeKind.Purged)], CancellationToken.None);
        await blobs.RemoveAsync(id, ct);
        return true;
    }
}
```

`IItemRepository` and `IItemChangeNotifier` (namespace `Struo.Application.Changes`, with `ItemChange`
and `ItemChangeKind`) are the core's own services. `IMyBlobs` stands for your own storage client. Register the handler the same way:

```csharp
builder.Services.AddScoped<ICollectionDeleteHandler, AttachmentDeleteHandler>();
```

## Collections that cannot be created

`[CmsCollection(Creatable = false)]` makes the generic items API refuse a create for that collection
with a `400` before it looks at the body. The core sets it on `file` and `user`, whose rows come from
`POST /api/files` and `POST /api/users`. See [Chapter 5: Collections](05-collections.md) for the flag,
and [Chapter 13](13-rest-endpoints.md) for how `/api/schema` exposes it as `creatable`.

## File storage: `IFileStorage`

The byte store behind the `file` collection is `IFileStorage`. The core ships a local and an S3 backend, chosen
by `Struo:Files:Backend`. To store files somewhere else, set the backend to `custom` and register your
implementation after `AddStruoCms()`. The core builds no storage in that mode, and startup fails with a message
naming the key when none is registered. Keep the contract in mind: a missing key throws
`FileBlobNotFoundException`, never the backend's own exception; `ListAsync` streams every blob;
`Describe()` is the line `files:sweep` prints, and must not contain a credential. A minimal sketch of an
Azure Blob storage (the `Azure.Storage.Blobs` package belongs to your fork, not to the core). It is untested: the
container must already exist, and presigned URLs (SAS) are left out:

```csharp
using System.Runtime.CompilerServices;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Struo.Application.Files;
using Struo.Domain.Query;

public sealed class AzureBlobFileStorage(BlobContainerClient container) : IFileStorage
{
    public bool SupportsPresignedUrls => false;

    public Task<string?> GetPresignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default) =>
        await container.GetBlobClient(key).UploadAsync(
            content, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, ct);

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
    {
        try
        {
            return await container.GetBlobClient(key).OpenReadAsync(cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            throw new FileBlobNotFoundException(key);   // the contract: a missing key is never a backend exception
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default) =>
        await container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: ct);

    public async IAsyncEnumerable<StoredBlob> ListAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var blob in container.GetBlobsAsync(cancellationToken: ct))
            yield return new StoredBlob(blob.Name, blob.Properties.LastModified?.UtcDateTime ?? DateTime.MaxValue);
    }

    // Shown by files:sweep: the container and account, never a key or a connection string.
    public string Describe() => $"azure blob, container {container.Name}, account {container.AccountName}";
}
```

Set `Struo:Files:Backend` to `custom` in configuration (for example `Struo__Files__Backend=custom`), and in
`Program.cs` register the storage after `AddStruoCms()`:

```csharp
builder.AddStruoCms();
builder.Services.AddSingleton(new BlobContainerClient(builder.Configuration["Azure:Storage:ConnectionString"], "media"));
builder.Services.AddSingleton<IFileStorage, AzureBlobFileStorage>();
```

See [Chapter 15](15-files-and-media.md) for the behaviour of the built-in backends and `files:sweep`.

## Your own errors: `StruoDomainException`

A fork's own client-facing error derives from `StruoDomainException`, which carries a code, a 400 to 499 status
and a message. REST and GraphQL both return them unchanged. How to define one, and the rules for the message and
the code, are in [Chapter 12](12-rest-conventions.md#your-own-error-codes).

## The GraphQL schema: `ConfigureGraphQl`

The third seam is not an interface but a hook on the builder. `ConfigureGraphQl` receives
HotChocolate's own `IRequestExecutorBuilder`, after the core schema is registered and before the
server is built, so a fork can add query and mutation fields and its own object types. It exposes
HotChocolate's API directly; what you can do with it is whatever HotChocolate allows.

```csharp
using HotChocolate.Types;

public sealed record BuildInfo(string Version);

[ExtendObjectType("Query")]
public sealed class BuildInfoQuery
{
    public BuildInfo GetBuildInfo() => new("1.4.2");
}
```

Register it in `Program.cs` through the callback of `AddStruoCms()`:

```csharp
builder.AddStruoCms(cms => cms.ConfigureGraphQl(graphQl => graphQl
    .AddTypeExtension<BuildInfoQuery>()));
```

`{ buildInfo { version } }` is then served at `/graphql` next to the generated fields.

A field you add to `Mutation` is yours to protect. The CMS's sign-in check and permissions are
attached to the fields it generates, not to the `Mutation` type, so a fork's own field runs for an
anonymous caller unless it checks. Your actions also run after the core's, so a setting you change on
the builder (introspection, the cost and depth rules, the dependency-injection scopes) replaces the
core's. CSRF protection is HTTP middleware and still applies. A mutation that requires a signed-in
caller:

```csharp
[ExtendObjectType("Mutation")]
public sealed class BuildInfoMutation
{
    public string RefreshBuildInfo([Service] IHttpContextAccessor accessor) =>
        accessor.HttpContext?.User.Identity?.IsAuthenticated == true
            ? "refreshed"
            : throw new GraphQLException("Sign in to run this mutation.");
}
```

Names must not collide with the generated schema; startup fails with a `GraphQL name collision`
message when one does. The query depth and cost limits, which a heavier fork query may need to
raise, are `GraphQl:MaxExecutionDepth`, `GraphQl:MaxFieldCost` and `GraphQl:MaxTypeCost`. See
[Chapter 14: GraphQL API](14-graphql.md).

## What's next

That covers the registration point, search providers, change notifications, write interceptors and delete handlers, file storage, domain errors and the GraphQL hook. The next chapter,
[Chapter 19: Admin Customization](19-admin-customization.md), comes back to the admin SPA, and
covers which parts of its appearance and behavior can be changed by configuration alone, and
which really need a change under `frontend/src`.
