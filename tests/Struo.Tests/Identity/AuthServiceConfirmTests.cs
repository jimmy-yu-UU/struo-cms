using AwesomeAssertions;
using Struo.Application.Security;
using Xunit;

namespace Struo.Tests.Identity;

public class AuthServiceConfirmTests
{
    private const string Email = "a@b.com";
    private const string Hash = "enc:pw";

    private sealed class MutableStore(UserCredential? current) : IUserCredentialStore
    {
        public UserCredential? Current { get; set; } = current;

        public Task<UserCredential?> FindByEmailAsync(string email, CancellationToken ct = default) =>
            Task.FromResult(Current);

        public Task<UserCredential?> FindByAccessTokenAsync(string tokenHash, CancellationToken ct = default) =>
            Task.FromResult<UserCredential?>(null);

        public Task<UserCredential?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Current);

        public Task TouchAccessTokenLastUsedAsync(Guid userId, DateTime nowUtc, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class PlainHasher : IPasswordHasher
    {
        public string Hash(string p) => "enc:" + p;
        public bool Verify(string encoded, string p) => encoded == "enc:" + p;
    }

    private static async Task<(AuthService Service, MutableStore Store, AuthResult Result, Guid Id)> LoginAsync()
    {
        var id = Guid.CreateVersion7();
        var store = new MutableStore(new UserCredential(id, Hash, true, Email));
        var service = new AuthService(store, new PlainHasher());
        return (service, store, await service.AuthenticateAsync(Email, "pw"), id);
    }

    [Fact]
    public async Task An_unchanged_credential_is_confirmed()
    {
        var (service, _, result, _) = await LoginAsync();
        (await service.ConfirmUnchangedAsync(result)).Should().BeTrue();
    }

    [Fact]
    public async Task A_changed_password_hash_is_not_confirmed()
    {
        var (service, store, result, id) = await LoginAsync();
        store.Current = new UserCredential(id, "enc:other", true, Email);
        (await service.ConfirmUnchangedAsync(result)).Should().BeFalse();
    }

    [Fact]
    public async Task An_edit_that_leaves_the_hash_alone_is_still_confirmed()
    {
        var (service, store, result, id) = await LoginAsync();
        store.Current = new UserCredential(id, Hash, true, "renamed@b.com");
        (await service.ConfirmUnchangedAsync(result)).Should().BeTrue();
    }

    [Fact]
    public async Task A_deactivated_user_is_not_confirmed()
    {
        var (service, store, result, id) = await LoginAsync();
        store.Current = new UserCredential(id, Hash, false, Email);
        (await service.ConfirmUnchangedAsync(result)).Should().BeFalse();
    }

    [Fact]
    public async Task A_removed_user_is_not_confirmed()
    {
        var (service, store, result, _) = await LoginAsync();
        store.Current = null;
        (await service.ConfirmUnchangedAsync(result)).Should().BeFalse();
    }

    [Fact]
    public async Task A_result_without_a_proof_is_never_confirmed()
    {
        var (service, _, _, id) = await LoginAsync();
        (await service.ConfirmUnchangedAsync(AuthResult.Ok(id))).Should().BeFalse();
        (await service.ConfirmUnchangedAsync(AuthResult.Fail(AuthFailure.InvalidCredentials))).Should().BeFalse();
    }

    [Fact]
    public async Task The_result_never_prints_the_stored_hash()
    {
        var (_, _, result, _) = await LoginAsync();
        result.ToString().Should().NotContain(Hash);
    }
}
