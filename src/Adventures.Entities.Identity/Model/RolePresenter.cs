// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Entities;

/// <summary>
/// The presenter of the Roles list. Read-only for now: the list and a role form are shown, but nothing offers to create,
/// change or delete a role (editing roles and assigning them to users arrives with the next slice).
/// </summary>
public sealed class RolePresenter(IEntityBll<Role> bll, EntitySchemaOf<Role> schema, IEntityRelationProvider? relations = null)
    : EntityPresenter<Role>(bll, schema.Schema, s => new Role(s), relations)
{
    protected override bool CanCreate => false;

    protected override EntityPermissions RowPermissions => EntityPermissions.ReadOnly;

    protected override EntityPermissions PermissionsFor(Role entity) => EntityPermissions.ReadOnly;
}
