// tests/Struo.Tests/GraphQl/AuthenticatedHttpContextAccessor.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Struo.Tests.GraphQl;

/// <summary>
/// Gives an in-process GraphQL executor a signed-in caller, which every mutation requires.
/// </summary>
internal sealed class AuthenticatedHttpContextAccessor : IHttpContextAccessor
{
    public HttpContext? HttpContext { get; set; } = new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity("test")),
    };
}
