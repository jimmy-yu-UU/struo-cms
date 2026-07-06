namespace Struo.Domain.Metadata.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CmsCollectionAttribute(string label) : Attribute
{
    public string Label { get; } = label;
    public string? Icon { get; set; }
    public string? Group { get; set; }
    public string? DefaultDisplayField { get; set; }

    /// <summary>
    /// When true, writes (create/update/delete) through the generic CRUD path require a super-admin,
    /// regardless of any per-collection RBAC grant. Used for the identity/authorization tables
    /// (user/role/permission/userRole) so a delegated write grant cannot be turned into
    /// self-escalation (e.g. inserting a userRole row that assigns oneself the super-admin role).
    /// Reads remain governed by ordinary RBAC.
    /// </summary>
    public bool AdminOnly { get; set; }
}
