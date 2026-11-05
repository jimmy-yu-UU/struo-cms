namespace Struo.Application.Security;

/// <summary>One action permission a role can hold, separate from the per-collection read/write/delete
/// grants. <see cref="LabelKey"/> and <see cref="DescriptionKey"/> are i18n keys; the admin UI supplies
/// the text.</summary>
public sealed record SystemCapability(string Code, string LabelKey, string DescriptionKey);

/// <summary>The capabilities the core defines. Forks add their own through
/// <see cref="ISystemCapabilitySource"/>; <see cref="SystemCapabilityCatalog"/> is the complete list.</summary>
public static class SystemCapabilities
{
    /// <summary>Lets the holder list and clear locked login accounts. It does not reach a super-admin
    /// account: only a super-admin can clear that lock.</summary>
    public const string LoginUnlock = "login.unlock";

    public static IReadOnlyList<SystemCapability> Core { get; } =
    [
        new(LoginUnlock, "capabilities.login.unlock.label", "capabilities.login.unlock.description"),
    ];
}
