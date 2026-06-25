// src/Struo.Api/Controllers/SchemaController.cs
using Microsoft.AspNetCore.Mvc;
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Models;

namespace Struo.Api.Controllers;

[ApiController]
[Route("api/schema")]
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
