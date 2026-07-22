using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
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
}
