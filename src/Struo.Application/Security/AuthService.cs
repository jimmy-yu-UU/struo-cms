namespace Struo.Application.Security;

public sealed class AuthService(IUserCredentialStore store, IPasswordHasher hasher) : IAuthService
{
    // A throwaway hash that equalizes response time when the account doesn't exist. Verifying
    // against it makes a missing user cost roughly the same Argon2 work as a wrong password, so an
    // attacker can't tell from timing whether an email is registered. Computed once, lazily.
    private static string? _dummyHash;

    public async Task<AuthResult> AuthenticateAsync(string email, string password, CancellationToken ct = default)
    {
        var cred = await store.FindByEmailAsync(email, ct);

        // A row the database matched under its collation counts only when its email is the presented one
        // after normalizing: the login lock is keyed on that spelling, so a collation-equivalent variant
        // would otherwise reach the account through a counter of its own.
        if (cred is null || string.IsNullOrWhiteSpace(cred.PasswordEncoded)
            || !EmailAddress.IsSameAccount(cred.Email, email))
        {
            // No account (or no password set): spend the same verify cost, then fail generically.
            hasher.Verify(DummyHash(hasher), password);
            return AuthResult.Fail(AuthFailure.InvalidCredentials);
        }

        if (!hasher.Verify(cred.PasswordEncoded, password))
            return AuthResult.Fail(AuthFailure.InvalidCredentials);
        if (!cred.IsActive)
            return AuthResult.Fail(AuthFailure.Inactive);
        return AuthResult.Ok(cred.Id, new PasswordProof(cred.PasswordEncoded));
    }

    public async Task<bool> ConfirmUnchangedAsync(AuthResult result, CancellationToken ct = default)
    {
        if (result is not { UserId: { } userId, Proof: { } proof }) return false;
        var current = await store.FindByIdAsync(userId, ct);
        return current is { IsActive: true }
            && string.Equals(current.PasswordEncoded, proof.StoredHash, StringComparison.Ordinal);
    }

    private static string DummyHash(IPasswordHasher hasher) =>
        _dummyHash ??= hasher.Hash("timing-equalizer-not-a-real-password");
}
