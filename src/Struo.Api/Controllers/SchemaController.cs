// src/Struo.Api/Controllers/SchemaController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Struo.Api.Auth;
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Models;

namespace Struo.Api.Controllers;

// The full content model (every collection/field, including ones only reachable by super-admins)
// was anonymously readable — a reconnaissance target, and inconsistent with production disabling
// GraphQL introspection (GraphQlServiceCollectionExtensions: DisableIntrospection). The frontend only
// ever calls GET /api/schema from post-login-only code paths (AppShell.onMounted, CollectionListView,
// ItemFormView, MediaDetailDialog, RelationPicker/RelatedList) which the router's authGuard never
// reaches while unauthenticated (unauthenticated -> redirected to /login before any of those mount).
// So, same as LanguagesController, this simply requires an authenticated caller rather than trying to
// filter the response to a "public" subset.
[ApiController]
[Route("api/schema")]
[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
public sealed class SchemaController(SchemaService schema) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<CollectionMetadata>> GetAll() => Ok(schema.GetAll());

    [HttpGet("{collection}")]
    public ActionResult<CollectionMetadata> Get(string collection)
    {
        var meta = schema.Get(collection);
        return meta is null ? NotFound() : Ok(meta);
    }
}
