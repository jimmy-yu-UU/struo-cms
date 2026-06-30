// src/Struo.Api/Controllers/ItemsController.cs
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Struo.Api.Auth;
using Struo.Application.Query;

namespace Struo.Api.Controllers;

[ApiController]
[Route("api/items/{collection}")]
public sealed class ItemsController(ItemService items) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(string collection, CancellationToken ct)
    {
        var qs = Request.Query.ToDictionary(k => k.Key, v => (string?)v.Value.ToString());
        var raw = QueryParser.ParseQueryString(qs);
        var result = await items.QueryAsync(collection, raw, Locale(), ct);
        return Ok(new { data = result.Data, meta = new { total = result.Total, limit = result.Limit, offset = result.Offset } });
    }

    [HttpPost("query")]
    public async Task<IActionResult> Query(string collection, [FromBody] JsonElement body, CancellationToken ct)
    {
        var raw = QueryParser.ParseEnvelope(body);
        var result = await items.QueryAsync(collection, raw, Locale(), ct);
        return Ok(new { data = result.Data, meta = new { total = result.Total, limit = result.Limit, offset = result.Offset } });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string collection, string id, CancellationToken ct)
    {
        var qs = Request.Query.ToDictionary(k => k.Key, v => (string?)v.Value.ToString());
        var deep = QueryParser.ParseQueryString(qs).Deep;
        var item = await items.GetAsync(collection, id, deep, Locale(), ct);
        return item is null ? NotFound() : Ok(new { data = item });
    }

    private string? Locale() =>
        Request.Query.TryGetValue("locale", out var lv) && !string.IsNullOrWhiteSpace(lv)
            ? lv.ToString()
            : null;

    [HttpPost]
    [Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
    public async Task<IActionResult> Create(string collection, [FromBody] JsonElement body, CancellationToken ct)
    {
        var created = await items.CreateAsync(collection, body, ct);
        var id = created.TryGetValue("id", out var idValue) ? idValue : null;
        return Created($"/api/items/{collection}/{id}", new { data = created });
    }

    [HttpPut("{id}")]
    [Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
    public async Task<IActionResult> Update(string collection, string id, [FromBody] JsonElement body, CancellationToken ct)
    {
        var updated = await items.UpdateAsync(collection, id, body, ct);
        return updated is null ? NotFound() : Ok(new { data = updated });
    }

    [HttpDelete("{id}")]
    [Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
    public async Task<IActionResult> Delete(string collection, string id, CancellationToken ct)
    {
        var ok = await items.DeleteAsync(collection, id, ct);
        return ok ? NoContent() : NotFound();
    }
}
