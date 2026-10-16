// src/Struo.Api/GraphQl/MutationAuthentication.cs
using HotChocolate.Resolvers;
using HotChocolate.Types.Descriptors.Configurations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Struo.Domain.Query;

namespace Struo.Api.GraphQl;

/// <summary>
/// Field middleware attached to every Mutation root field: a mutation needs an authenticated
/// caller regardless of the permissions the <c>public</c> role holds, matching the sign-in REST
/// requires on its write endpoints. An unauthenticated caller gets a
/// <see cref="PermissionDeniedException"/>, which <see cref="StruoErrorFilter"/> reports as
/// <c>UNAUTHORIZED</c>. No HttpContext (non-HTTP execution) counts as unauthenticated.
/// </summary>
internal static class MutationAuthentication
{
    internal static FieldMiddlewareConfiguration Configuration { get; } = new(Middleware);

    internal static FieldDelegate Middleware(FieldDelegate next) => ctx =>
    {
        var user = ctx.Services.GetService<IHttpContextAccessor>()?.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            throw new PermissionDeniedException("Authentication is required to run mutations.");
        return next(ctx);
    };
}
