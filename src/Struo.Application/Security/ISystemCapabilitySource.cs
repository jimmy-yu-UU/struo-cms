namespace Struo.Application.Security;

/// <summary>Adds capabilities to <see cref="SystemCapabilityCatalog"/>. Register with
/// <c>services.AddSingleton&lt;ISystemCapabilitySource, MySource&gt;()</c> (any number); a code that repeats
/// another source's or a core one fails the first resolution of the catalog.</summary>
public interface ISystemCapabilitySource
{
    IReadOnlyList<SystemCapability> Capabilities { get; }
}
