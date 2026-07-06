using AwesomeAssertions;
using Struo.Application.Security;
using Xunit;

namespace Struo.Tests.Identity;

public class AuthServiceTests
{
    private sealed class FakeStore(UserCredential? byEmail) : IUserCredentialStore
    {
        public Task<UserCredential?> FindByEmailAsync(string email, CancellationToken ct = default) => Task.FromResult(byEmail);
        public Task<UserCredential?> FindByAccessTokenAsync(string tokenHash, CancellationToken ct = default) => Task.FromResult<UserCredential?>(null);
        public Task TouchAccessTokenLastUsedAsync(Guid userId, DateTime nowUtc, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class PlainHasher : IPasswordHasher
    {
        public string Hash(string p) => "enc:" + p;
        public bool Verify(string encoded, string p) => encoded == "enc:" + p;
    }

    [Fact]
    public async Task Valid_credentials_succeed()
    {
        var id = Guid.CreateVersion7();
        var svc = new AuthService(new FakeStore(new UserCredential(id, "enc:pw", true)), new PlainHasher());
        var r = await svc.AuthenticateAsync("a@b.com", "pw");
        r.Succeeded.Should().BeTrue();
        r.UserId.Should().Be(id);
    }

    [Fact]
    public async Task Wrong_password_fails_invalid()
    {
        var svc = new AuthService(new FakeStore(new UserCredential(Guid.CreateVersion7(), "enc:pw", true)), new PlainHasher());
        (await svc.AuthenticateAsync("a@b.com", "nope")).Failure.Should().Be(AuthFailure.InvalidCredentials);
    }

    [Fact]
    public async Task Unknown_email_fails_invalid()
    {
        var svc = new AuthService(new FakeStore(null), new PlainHasher());
        (await svc.AuthenticateAsync("x@b.com", "pw")).Failure.Should().Be(AuthFailure.InvalidCredentials);
    }

    [Fact]
    public async Task Inactive_user_fails_inactive()
    {
        var svc = new AuthService(new FakeStore(new UserCredential(Guid.CreateVersion7(), "enc:pw", false)), new PlainHasher());
        (await svc.AuthenticateAsync("a@b.com", "pw")).Failure.Should().Be(AuthFailure.Inactive);
    }

    [Fact]
    public async Task Empty_stored_hash_is_invalid_credentials_without_calling_verify()
    {
        var userId = Guid.NewGuid();
        var store = new FakeCredentialStore(new UserCredential(userId, "", IsActive: true));
        var hasher = new ThrowingHasher(); // Verify must never be called
        var sut = new AuthService(store, hasher);

        var result = await sut.AuthenticateAsync("jit@corp.com", "anything");

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(AuthFailure.InvalidCredentials);
        hasher.VerifyCalled.Should().BeFalse();
    }

    private sealed class FakeCredentialStore(UserCredential? cred) : IUserCredentialStore
    {
        public Task<UserCredential?> FindByEmailAsync(string email, CancellationToken ct = default) => Task.FromResult(cred);
        public Task<UserCredential?> FindByAccessTokenAsync(string tokenHash, CancellationToken ct = default) => Task.FromResult<UserCredential?>(null);
        public Task TouchAccessTokenLastUsedAsync(Guid userId, DateTime nowUtc, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ThrowingHasher : IPasswordHasher
    {
        public bool VerifyCalled { get; private set; }
        public string Hash(string password) => throw new NotSupportedException();
        public bool Verify(string encoded, string password) { VerifyCalled = true; throw new InvalidOperationException("Verify should not be called for an empty hash"); }
    }
}
