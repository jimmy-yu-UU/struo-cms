using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Struo.Api.Auth;
using Struo.Application.Abstractions;
using Struo.Application.Security;
using Xunit;

namespace Struo.Tests.Files;

// BL-1: FileAccessPolicy is the extracted RBAC decision-owner for the "file" collection, absorbing
// the permission-related dependencies FilesController used to hold directly. These unit tests cover
// the parts reachable without a full authentication host (CanWrite/CanDelete delegation, and the
// cookie/"already authenticated" fast path of CanReadUnpublishedAsync); the bearer-adopt path is
// exercised end-to-end by FileUnpublishedRbacTests, which stayed green across this refactor.
public class FileAccessPolicyTests
{
    private sealed class FakePermissionService(bool read, bool write, bool delete) : IPermissionService
    {
        public bool CanRead(string collection) => read;
        public bool CanWrite(string collection) => write;
        public bool CanDelete(string collection) => delete;
        public IReadOnlyCollection<string> ReadableFields(string collection, IEnumerable<string> allFieldNames) =>
            allFieldNames.ToList();
    }

    private sealed class FakeCurrentUserAccessor(Guid? userId) : ICurrentUserAccessor
    {
        public Guid? GetCurrentUserId() => userId;
    }

    private sealed class UnusedRolePermissionStore : IRolePermissionStore
    {
        public Task<RolePermissionData> LoadForUserAsync(Guid? userId, CancellationToken ct = default) =>
            throw new InvalidOperationException("Not expected to be called on the fast (already-authenticated) path.");

        public Task<RolePermissionData> LoadForRolesAsync(IReadOnlyList<Guid> roleIds, CancellationToken ct = default) =>
            throw new InvalidOperationException("Not expected to be called on the fast (already-authenticated) path.");
    }

    [Fact]
    public void CanWrite_delegates_to_the_file_collection_grant()
    {
        var policy = new FileAccessPolicy(
            new FakePermissionService(read: false, write: true, delete: false),
            new FakeCurrentUserAccessor(null), new UnusedRolePermissionStore(), new CurrentPermissions());

        policy.CanWrite().Should().BeTrue();
    }

    [Fact]
    public void CanDelete_delegates_to_the_file_collection_grant()
    {
        var policy = new FileAccessPolicy(
            new FakePermissionService(read: false, write: false, delete: true),
            new FakeCurrentUserAccessor(null), new UnusedRolePermissionStore(), new CurrentPermissions());

        policy.CanDelete().Should().BeTrue();
    }

    [Fact]
    public async Task CanReadUnpublishedAsync_fast_path_reads_the_current_snapshot_when_already_authenticated()
    {
        // A cookie identity is already present (GetCurrentUserId() is non-null), so the method must
        // return permissions.CanRead("file") directly without probing the bearer scheme at all — the
        // fake IRolePermissionStore throwing proves that path is never touched.
        var policy = new FileAccessPolicy(
            new FakePermissionService(read: true, write: false, delete: false),
            new FakeCurrentUserAccessor(Guid.NewGuid()), new UnusedRolePermissionStore(), new CurrentPermissions());

        var result = await policy.CanReadUnpublishedAsync(new DefaultHttpContext(), CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanReadUnpublishedAsync_fast_path_denies_when_the_snapshot_lacks_read()
    {
        var policy = new FileAccessPolicy(
            new FakePermissionService(read: false, write: false, delete: false),
            new FakeCurrentUserAccessor(Guid.NewGuid()), new UnusedRolePermissionStore(), new CurrentPermissions());

        var result = await policy.CanReadUnpublishedAsync(new DefaultHttpContext(), CancellationToken.None);

        result.Should().BeFalse();
    }

    // BL-2: a defensive floor. In practice BearerTokenAuthenticationHandler always stamps a
    // NameIdentifier claim (see HttpContextCurrentUserAccessor), but if a bearer principal is ever
    // adopted WITHOUT one, GetCurrentUserId() resolves to null — which must never be treated as "no
    // permission snapshot yet resolved, fall through to the public floor". It must be denied outright,
    // never silently promoted to whatever CanRead("file") happens to be for the (still) anonymous
    // snapshot — the fake IRolePermissionStore throwing proves the resolve-and-set path is never
    // reached once the guard trips.
    private sealed class FakeAuthenticationService(ClaimsPrincipal principal) : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, scheme ?? AuthSchemes.Bearer)));

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            throw new InvalidOperationException("Not expected to be called.");

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            throw new InvalidOperationException("Not expected to be called.");

        public Task SignInAsync(
            HttpContext context, string? scheme, ClaimsPrincipal principal2, AuthenticationProperties? properties) =>
            throw new InvalidOperationException("Not expected to be called.");

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            throw new InvalidOperationException("Not expected to be called.");
    }

    [Fact]
    public async Task CanReadUnpublishedAsync_bearer_path_denies_when_adopted_principal_has_no_user_id()
    {
        // A bearer principal with NO NameIdentifier claim — GetCurrentUserId() will read back null
        // from the REAL accessor (wired below via IHttpContextAccessor), matching how the production
        // code resolves it after adopting httpContext.User = bearer.Principal.
        var identity = new ClaimsIdentity(authenticationType: "Bearer"); // no claims at all
        var principalWithoutUserId = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext();
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(new FakeAuthenticationService(principalWithoutUserId));
        httpContext.RequestServices = services.BuildServiceProvider();

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var policy = new FileAccessPolicy(
            new FakePermissionService(read: true, write: false, delete: false),
            new HttpContextCurrentUserAccessor(accessor),
            new UnusedRolePermissionStore(),
            new CurrentPermissions());

        var result = await policy.CanReadUnpublishedAsync(httpContext, CancellationToken.None);

        result.Should().BeFalse("a bearer principal with no resolvable user id must never fall through to the public floor");
    }
}
