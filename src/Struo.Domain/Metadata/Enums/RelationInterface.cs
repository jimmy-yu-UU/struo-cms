namespace Struo.Domain.Metadata.Enums;

/// <summary>
/// How the admin SPA edits a relation. Every member must have a matching entry in
/// <c>frontend/src/lib/relationInputKind.ts</c>'s map — an unmapped member falls back to
/// <c>'readonly'</c> there, so the relation silently becomes uneditable rather than failing loudly.
/// <c>frontend/tests/schemaContract.test.ts</c> enforces this against <c>schema/interfaces.json</c>.
///
/// FilePicker/ImagePicker/FilesPicker were removed: nothing ever assigned them, no frontend mapping
/// existed, and file references are modelled as <see cref="FieldInterface"/> File/Image/Files rather
/// than as relations. Re-adding a member here without the frontend half fails the contract gate.
/// </summary>
public enum RelationInterface { Dropdown, TagSelect, TreeSelect, RelatedList }
