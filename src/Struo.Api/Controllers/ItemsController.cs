// src/Struo.Api/Controllers/ItemsController.cs
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Struo.Api.Auth;
using Struo.Application.Query;
using Struo.Application.Security;
using Struo.Domain.Query;

namespace Struo.Api.Controllers;

[ApiController]
[Route("api/items/{collection}")]
public sealed class ItemsController(ItemService items, IPermissionService permissions) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(string collection, CancellationToken ct)
    {
        var qs = Request.Query.ToDictionary(k => k.Key, v => (string?)v.Value.ToString());
        var raw = QueryParser.ParseQueryString(qs);
        var mode = DeletedMode(collection);
        var result = await items.QueryAsync(collection, raw, Locale(), mode, ct);
        return Ok(new Struo.Api.Http.PagedResult(result.Data, result.Total, result.Limit, result.Offset));
    }

    [HttpPost("query")]
    public async Task<IActionResult> Query(string collection, [FromBody] JsonElement body, CancellationToken ct)
    {
        var raw = QueryParser.ParseEnvelope(body);
        var mode = DeletedMode(collection);
        var result = await items.QueryAsync(collection, raw, Locale(), mode, ct);
        return Ok(new Struo.Api.Http.PagedResult(result.Data, result.Total, result.Limit, result.Offset));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string collection, string id, CancellationToken ct)
    {
        var qs = Request.Query.ToDictionary(k => k.Key, v => (string?)v.Value.ToString());
        var deep = QueryParser.ParseQueryString(qs).Deep;
        var mode = DeletedMode(collection);
        var item = await items.GetAsync(collection, id, deep, Locale(), mode, ct);
        return item is null ? NotFound() : Ok(item);
    }

    private string? Locale() =>
        Request.Query.TryGetValue("locale", out var lv) && !string.IsNullOrWhiteSpace(lv)
            ? lv.ToString()
            : null;

    /// <summary>
    /// Parses the <c>?deleted=exclude|only|with</c> query parameter (default exclude). Requesting
    /// <c>only</c>/<c>with</c> exposes soft-deleted rows, so it requires delete permission on the
    /// collection — <see cref="ItemService.QueryAsync"/>/<see cref="ItemService.GetAsync"/> only
    /// check <c>CanRead</c>, so the stricter gate lives here.
    /// </summary>
    private DeletedFilter DeletedMode(string collection)
    {
        var mode = ParseDeletedMode();
        DeletedAccessGuard.EnsureCanViewDeleted(permissions, collection, mode);
        return mode;
    }

    private DeletedFilter ParseDeletedMode()
    {
        if (!Request.Query.TryGetValue("deleted", out var dv) || string.IsNullOrWhiteSpace(dv))
            return DeletedFilter.Exclude;
        return dv.ToString().ToLowerInvariant() switch
        {
            "exclude" => DeletedFilter.Exclude,
            "only" => DeletedFilter.Only,
            "with" => DeletedFilter.With,
            _ => throw new QueryException("Query parameter 'deleted' must be exclude|only|with.")
        };
    }

    [HttpPost]
    [Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
    public async Task<IActionResult> Create(string collection, [FromBody] JsonElement body, CancellationToken ct)
    {
        var created = await items.CreateAsync(collection, body, ct);
        var id = created.TryGetValue("id", out var idValue) ? idValue : null;
        return Created($"/api/items/{collection}/{id}", created);
    }

    [HttpPut("{id}")]
    [Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
    public async Task<IActionResult> Update(string collection, string id, [FromBody] JsonElement body, CancellationToken ct)
    {
        var updated = await items.UpdateAsync(collection, id, body, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id}")]
    [Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
    public async Task<IActionResult> Delete(string collection, string id, CancellationToken ct)
    {
        var purge = Request.Query.TryGetValue("purge", out var pv)
                    && string.Equals(pv.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        var ok = await items.DeleteAsync(collection, id, purge, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpPost("{id}/restore")]
    [Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
    public async Task<IActionResult> Restore(string collection, string id, CancellationToken ct)
    {
        var restored = await items.RestoreAsync(collection, id, ct);
        return restored is null ? NotFound() : Ok(restored);
    }

    [HttpGet("{id}/revisions")]
    public async Task<IActionResult> Revisions(string collection, string id, CancellationToken ct)
    {
        var list = await items.ListRevisionsAsync(collection, id, ct);
        return Ok(list);   // EnvelopeResultFilter wraps -> { success, data: [ { revisionNumber, operation, createdAt, createdBy } ] }
    }

    [HttpGet("{id}/revisions/{revisionNumber:long}")]
    public async Task<IActionResult> Revision(string collection, string id, long revisionNumber, CancellationToken ct)
    {
        var rec = await items.GetRevisionAsync(collection, id, revisionNumber, ct);
        if (rec is null) return NotFound();
        // Emit the stored snapshot as structured JSON (not a quoted string).
        using var snapshotDoc = JsonDocument.Parse(rec.Snapshot);
        return Ok(new
        {
            rec.RevisionNumber,
            rec.Operation,
            rec.CreatedAt,
            rec.CreatedBy,
            snapshot = snapshotDoc.RootElement.Clone()   // Clone so the value survives the using-scope dispose
        });
    }

    [HttpPost("{id}/revisions/{revisionNumber:long}/revert")]
    [Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
    public async Task<IActionResult> Revert(string collection, string id, long revisionNumber, CancellationToken ct)
    {
        var reverted = await items.RevertAsync(collection, id, revisionNumber, ct);
        return reverted is null ? NotFound() : Ok(reverted);
    }
}
