using System.Text.Json.Serialization;

namespace Struo.Domain.Metadata.Models;

/// <summary>
/// A single free-form tag value with an optional manual display label. When <see cref="Label"/>
/// is null the raw <see cref="Value"/> is displayed. This is the CLR shape stored (as a JSON
/// array) for a <c>Tags</c> field interface. Plain BCL record — no external dependency.
/// </summary>
public sealed record TagItem(
    string Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Label = null);
