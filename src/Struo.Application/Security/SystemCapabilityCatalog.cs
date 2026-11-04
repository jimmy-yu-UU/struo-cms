namespace Struo.Application.Security;

/// <summary>The one list of capabilities a role can hold: the core ones plus every
/// <see cref="ISystemCapabilitySource"/>. Role capability writes validate against it. Codes are
/// compared exactly, with no case folding.</summary>
public sealed class SystemCapabilityCatalog
{
    /// <summary>Longest code the <c>capability</c> column holds.</summary>
    public const int MaxCodeLength = 255;

    private readonly HashSet<string> _codes;

    public SystemCapabilityCatalog(IEnumerable<ISystemCapabilitySource> sources)
    {
        var all = new List<SystemCapability>();
        _codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var capability in SystemCapabilities.Core.Concat(sources.SelectMany(s => s.Capabilities)))
        {
            if (string.IsNullOrWhiteSpace(capability.Code) || capability.Code.Length > MaxCodeLength)
                throw new InvalidOperationException(
                    $"A system capability code must be 1 to {MaxCodeLength} characters and not blank.");
            if (!_codes.Add(capability.Code))
                throw new InvalidOperationException($"The system capability '{capability.Code}' is defined twice.");
            all.Add(capability);
        }
        All = all;
    }

    public IReadOnlyList<SystemCapability> All { get; }

    public bool IsKnown(string code) => _codes.Contains(code);
}
