namespace Struo.Application.Security;

public interface IExternalLoginService
{
    Task<ExternalLoginResult> ResolveOrProvisionAsync(
        ExternalIdentity identity, ExternalLoginPolicy policy, CancellationToken ct = default);
}
