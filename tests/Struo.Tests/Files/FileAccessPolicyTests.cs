using AwesomeAssertions;
using Struo.Api.Auth;
using Struo.Application.Abstractions;
using Struo.Application.Security;
using Xunit;

namespace Struo.Tests.Files;

// FileAccessPolicy is the extracted RBAC decision-owner for the "file" collection, absorbing
// the permission-related dependencies FilesController used to hold directly. CanReadUnpublished is a
// plain synchronous predicate over the already-resolved per-request identity/permission snapshot — the
// AuthSchemes.Adaptive policy scheme (see BearerReadAuthenticationTests) is what guarantees a bearer
// caller's identity and grants are resolved before this runs, so no scheme probing happens here.
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

    [Fact]
    public void CanWrite_delegates_to_the_file_collection_grant()
    {
        var policy = new FileAccessPolicy(
            new FakePermissionService(read: false, write: true, delete: false),
            new FakeCurrentUserAccessor(null));

        policy.CanWrite().Should().BeTrue();
    }

    [Fact]
    public void CanDelete_delegates_to_the_file_collection_grant()
    {
        var policy = new FileAccessPolicy(
            new FakePermissionService(read: false, write: false, delete: true),
            new FakeCurrentUserAccessor(null));

        policy.CanDelete().Should().BeTrue();
    }

    [Fact]
    public void CanReadUnpublished_allows_an_authenticated_caller_with_file_write()
    {
        var policy = new FileAccessPolicy(
            new FakePermissionService(read: true, write: true, delete: false),
            new FakeCurrentUserAccessor(Guid.NewGuid()));

        policy.CanReadUnpublished().Should().BeTrue();
    }

    [Fact]
    public void CanReadUnpublished_denies_an_authenticated_caller_with_read_but_no_write()
    {
        var policy = new FileAccessPolicy(
            new FakePermissionService(read: true, write: false, delete: false),
            new FakeCurrentUserAccessor(Guid.NewGuid()));

        policy.CanReadUnpublished().Should().BeFalse("a public-floor read grant must not unlock drafts");
    }

    [Fact]
    public void CanReadUnpublished_denies_an_anonymous_caller()
    {
        var policy = new FileAccessPolicy(
            new FakePermissionService(read: true, write: true, delete: false),
            new FakeCurrentUserAccessor(null));

        policy.CanReadUnpublished().Should().BeFalse("an unauthenticated caller must never read drafts");
    }
}
