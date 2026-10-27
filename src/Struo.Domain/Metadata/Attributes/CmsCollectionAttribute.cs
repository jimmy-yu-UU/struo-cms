namespace Struo.Domain.Metadata.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CmsCollectionAttribute(string label) : Attribute
{
    public string Label { get; } = label;
    public string? Icon { get; set; }
    public string? Group { get; set; }

    /// <summary>
    /// Pins the collection name (REST route segment, permission grants, revisions, GraphQL names) so a
    /// C# class rename cannot change it. Must match <c>^[a-z][A-Za-z0-9]*$</c> and be unique across all
    /// collections. When unset, the name is the camel-cased class name.
    /// </summary>
    public string? Name { get; set; }
    public string? DefaultDisplayField { get; set; }

    /// <summary>
    /// When true, writes (create/update/delete) through the generic CRUD path require a super-admin,
    /// regardless of any per-collection RBAC grant. Used for the identity/authorization tables
    /// (user/role/permission/userRole) so a delegated write grant cannot be turned into
    /// self-escalation (e.g. inserting a userRole row that assigns oneself the super-admin role).
    /// Reads remain governed by ordinary RBAC.
    /// </summary>
    public bool AdminOnly { get; set; }

    /// <summary>
    /// When true, the collection is omitted from the admin sidebar/nav. It stays fully reachable
    /// via REST/GraphQL and direct admin URLs — this is a presentation flag, not an access rule.
    /// Used for collections whose admin surface lives elsewhere (File → media library) or that are
    /// implementation details behind a dedicated editor (Permission/UserRole → Role permission
    /// matrix, User.Roles TagSelect).
    /// </summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// When false, the generic items API (REST and GraphQL) refuses to create rows in this collection,
    /// and the admin UI offers no generic "New" form for it. Reads, updates and deletes are unaffected.
    /// For collections whose rows come from a dedicated pipeline (uploads, account creation). Defaults
    /// to true.
    /// </summary>
    public bool Creatable { get; set; } = true;

    /// <summary>
    /// When true, the collection keeps a revision history: every successful create/update appends a
    /// complete snapshot of the item's post-write state to the framework `Revision` table, and any past
    /// revision can be re-applied via revert. Opt-in; snapshots live in a shared table, so —
    /// unlike soft delete — nothing is added to the entity, hence an attribute flag rather than an interface.
    /// </summary>
    public bool Revisions { get; set; }
}
