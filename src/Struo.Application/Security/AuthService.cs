namespace Struo.Application.Security;

public sealed class AuthService(IUserCredentialStore store, IPasswordHasher hasher) : IAuthService
{
    public async Task<AuthResult> AuthenticateAsync(string email, string password, CancellationToken ct = default)
    {
        var cred = await store.FindByEmailAsync(email, ct);
        if (cred is null || string.IsNullOrWhiteSpace(cred.PasswordEncoded) || !hasher.Verify(cred.PasswordEncoded, password))
            return AuthResult.Fail(AuthFailure.InvalidCredentials);
        if (!cred.IsActive)
            return AuthResult.Fail(AuthFailure.Inactive);
        return AuthResult.Ok(cred.Id);
    }
}
