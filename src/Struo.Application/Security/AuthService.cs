namespace Struo.Application.Security;

public sealed class AuthService(IUserCredentialStore store, IPasswordHasher hasher) : IAuthService
{
    // A throwaway hash used to equalize response time when the account doesn't exist. Verifying
    // against it makes a missing user cost roughly the same Argon2 work as a wrong password, so an
    // attacker can't tell from timing whether an email is registered. Computed once, lazily.
    private static string? _dummyHash;

    public async Task<AuthResult> AuthenticateAsync(string email, string password, CancellationToken ct = default)
    {
        var cred = await store.FindByEmailAsync(email, ct);

        if (cred is null || string.IsNullOrWhiteSpace(cred.PasswordEncoded))
        {
            // No account (or no password set): spend the same verify cost, then fail generically.
            _dummyHash ??= hasher.Hash("timing-equalizer-not-a-real-password");
            hasher.Verify(_dummyHash, password);
            return AuthResult.Fail(AuthFailure.InvalidCredentials);
        }

        if (!hasher.Verify(cred.PasswordEncoded, password))
            return AuthResult.Fail(AuthFailure.InvalidCredentials);
        if (!cred.IsActive)
            return AuthResult.Fail(AuthFailure.Inactive);
        return AuthResult.Ok(cred.Id);
    }
}
