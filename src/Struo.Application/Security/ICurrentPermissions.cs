namespace Struo.Application.Security;

/// <summary>Scoped holder for the current request's resolved permissions. Populated through
/// <see cref="ICurrentPermissionsWriter"/> once per request by the resolution middleware; read by
/// <see cref="RbacPermissionService"/> and by controllers that need an admin check.</summary>
public interface ICurrentPermissions
{
    EffectivePermissions Current { get; }
}

/// <summary>Write side of the scoped permissions holder. Only <c>PermissionResolutionMiddleware</c>
/// should be injected with this — everything else should only ever read <see cref="ICurrentPermissions"/>.</summary>
public interface ICurrentPermissionsWriter
{
    void Set(EffectivePermissions permissions);
}

public sealed class CurrentPermissions : ICurrentPermissions, ICurrentPermissionsWriter
{
    public EffectivePermissions Current { get; private set; } = EffectivePermissions.DenyAll;
    public void Set(EffectivePermissions permissions) => Current = permissions;
}
