using AwesomeAssertions;
using Struo.Application.Security;
using Xunit;

namespace Struo.Tests.Identity;

public class ExternalLoginServiceTests
{
    private static ExternalLoginPolicy Policy(
        bool requireVerified = false, string? tenant = null, string[]? domains = null) =>
        new(requireVerified, tenant, domains ?? []);

    private sealed class FakeStore : IExternalUserStore
    {
        private readonly ExternalUserMatch? _match;
        public FakeStore(ExternalUserMatch? match = null) => _match = match;
        public string? CreatedEmail { get; private set; }
        public Guid CreatedId { get; } = Guid.CreateVersion7();
        public Task<ExternalUserMatch?> FindByEmailAsync(string email, CancellationToken ct = default) => Task.FromResult(_match);
        public Task<Guid> CreateExternalUserAsync(string email, string? name, CancellationToken ct = default)
        { CreatedEmail = email; return Task.FromResult(CreatedId); }
    }

    private static ExternalIdentity Id(
        string? email = "alice@corp.com", bool? verified = null, string? tid = null, string? name = "Alice") =>
        new("https://idp", tid, email, verified, name);

    [Fact]
    public async Task Existing_active_user_resolves_without_provisioning()
    {
        var existing = new ExternalUserMatch(Guid.CreateVersion7(), IsActive: true);
        var sut = new ExternalLoginService(new FakeStore(existing));
        var r = await sut.ResolveOrProvisionAsync(Id(), Policy());
        r.Succeeded.Should().BeTrue();
        r.Provisioned.Should().BeFalse();
        r.UserId.Should().Be(existing.Id);
    }

    [Fact]
    public async Task Missing_user_is_jit_provisioned()
    {
        var store = new FakeStore(match: null);
        var sut = new ExternalLoginService(store);
        var r = await sut.ResolveOrProvisionAsync(Id(email: "new@corp.com"), Policy());
        r.Succeeded.Should().BeTrue();
        r.Provisioned.Should().BeTrue();
        r.UserId.Should().Be(store.CreatedId);
        store.CreatedEmail.Should().Be("new@corp.com");
    }

    [Fact]
    public async Task Existing_inactive_user_is_rejected()
    {
        var sut = new ExternalLoginService(new FakeStore(new ExternalUserMatch(Guid.CreateVersion7(), IsActive: false)));
        var r = await sut.ResolveOrProvisionAsync(Id(), Policy());
        r.Succeeded.Should().BeFalse();
        r.Failure.Should().Be(ExternalLoginFailure.Inactive);
    }

    [Fact]
    public async Task No_email_fails()
    {
        var sut = new ExternalLoginService(new FakeStore());
        var r = await sut.ResolveOrProvisionAsync(Id(email: null), Policy());
        r.Failure.Should().Be(ExternalLoginFailure.NoEmail);
    }

    [Fact]
    public async Task Require_email_verified_rejects_unverified()
    {
        var sut = new ExternalLoginService(new FakeStore());
        var r = await sut.ResolveOrProvisionAsync(Id(verified: null), Policy(requireVerified: true));
        r.Failure.Should().Be(ExternalLoginFailure.EmailNotVerified);
    }

    [Fact]
    public async Task Tenant_mismatch_is_rejected()
    {
        var sut = new ExternalLoginService(new FakeStore());
        var r = await sut.ResolveOrProvisionAsync(Id(tid: "other-tenant"), Policy(tenant: "my-tenant"));
        r.Failure.Should().Be(ExternalLoginFailure.TenantNotAllowed);
    }

    [Fact]
    public async Task Domain_not_allowed_is_rejected()
    {
        var sut = new ExternalLoginService(new FakeStore());
        var r = await sut.ResolveOrProvisionAsync(Id(email: "bob@evil.com"), Policy(domains: ["corp.com"]));
        r.Failure.Should().Be(ExternalLoginFailure.DomainNotAllowed);
    }
}
