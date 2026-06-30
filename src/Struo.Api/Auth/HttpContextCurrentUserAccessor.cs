using System.Security.Claims;
using Struo.Application.Abstractions;

namespace Struo.Api.Auth;

public sealed class HttpContextCurrentUserAccessor(IHttpContextAccessor accessor) : ICurrentUserAccessor
{
    public Guid? GetCurrentUserId()
    {
        var id = accessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(id, out var g) ? g : null;
    }
}
