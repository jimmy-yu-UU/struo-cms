// src/Struo.Application/Query/Write/FieldValidatorRegistry.cs
using Struo.Domain.Metadata.Enums;

namespace Struo.Application.Query;

/// <summary>
/// Ordered per-<see cref="FieldInterface"/> validator phases for the write path. The phase ORDER is
/// observable (exception precedence when a body violates several fields) and reproduces the historical
/// <c>ItemService.Deserialize</c> sequence exactly:
/// <list type="number">
///   <item>First — multi-value fields (MultiSelect / CheckboxGroup / Tags) in ONE pass over
///     <c>meta.Fields</c> in declaration order (Tags → <see cref="TagsFieldValidator"/>, the option-bound
///     two → <see cref="OptionMultiValueFieldValidator"/>).</item>
///   <item>Then — KeyValue.</item>
///   <item>Then — Files.</item>
///   <item>Then — Repeater.</item>
/// </list>
/// </summary>
public sealed class FieldValidatorRegistry
{
    private static readonly TagsFieldValidator Tags = new();
    private static readonly OptionMultiValueFieldValidator OptionMultiValue = new();
    private static readonly KeyValueFieldValidator KeyValue = new();
    private static readonly FilesFieldValidator Files = new();
    private static readonly RepeaterFieldValidator Repeater = new();

    /// <summary>
    /// Each phase pairs the set of <see cref="FieldInterface"/>s it handles (used to filter
    /// <c>meta.Fields</c> for a single declaration-order pass) with the validator to dispatch each
    /// matching field to.
    /// </summary>
    public IReadOnlyList<(IReadOnlySet<FieldInterface> Interfaces, IReadOnlyDictionary<FieldInterface, IFieldValidator> Validators)> Phases { get; } =
    [
        (
            new HashSet<FieldInterface> { FieldInterface.MultiSelect, FieldInterface.CheckboxGroup, FieldInterface.Tags },
            new Dictionary<FieldInterface, IFieldValidator>
            {
                [FieldInterface.MultiSelect] = OptionMultiValue,
                [FieldInterface.CheckboxGroup] = OptionMultiValue,
                [FieldInterface.Tags] = Tags,
            }
        ),
        (
            new HashSet<FieldInterface> { FieldInterface.KeyValue },
            new Dictionary<FieldInterface, IFieldValidator> { [FieldInterface.KeyValue] = KeyValue }
        ),
        (
            new HashSet<FieldInterface> { FieldInterface.Files },
            new Dictionary<FieldInterface, IFieldValidator> { [FieldInterface.Files] = Files }
        ),
        (
            new HashSet<FieldInterface> { FieldInterface.Repeater },
            new Dictionary<FieldInterface, IFieldValidator> { [FieldInterface.Repeater] = Repeater }
        ),
    ];
}
