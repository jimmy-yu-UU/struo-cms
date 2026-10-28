// tests/Struo.Tests/Support/ContentAssemblyEnvBootstrap.cs
using System.Runtime.CompilerServices;

namespace Struo.Tests.Support;

/// <summary>
/// Pins the sample content assembly for every host in this suite. The environment is the only
/// override that reaches <c>AddStruoCms</c> in time; see <see cref="TestHostEnvironment"/>.
/// </summary>
internal static class ContentAssemblyEnvBootstrap
{
    [ModuleInitializer]
    internal static void PinRegistrationTimeConfigurationForTestHosts() =>
        TestHostEnvironment.Pin("Struo.Sample.Blog");
}
