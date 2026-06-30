namespace Struo.Application.Security;

/// <summary>Scoped holder for the current request's resolved permissions. Populated once per
/// request by the resolution middleware; read by <see cref="RbacPermissionService"/> and by
/// controllers that need an admin check.</summary>
public interface ICurrentPermissions
{
    EffectivePermissions Current { get; }
    void Set(EffectivePermissions permissions);
}

public sealed class CurrentPermissions : ICurrentPermissions
{
    public EffectivePermissions Current { get; private set; } = EffectivePermissions.DenyAll;
    public void Set(EffectivePermissions permissions) => Current = permissions;
}
