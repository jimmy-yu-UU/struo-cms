using System.Diagnostics.CodeAnalysis;

// Types in namespaces that merely start with System or Microsoft, and in no namespace at all, hold an
// object member; the scan has to walk them like any fork type.

namespace Systems.Foo
{
    public sealed class FooHolder
    {
        public object? Payload { get; set; }
    }
}

namespace MicrosoftPartner.Bar
{
    public sealed class BarHolder
    {
        public object? Payload { get; set; }
    }
}

[SuppressMessage("Design", "CA1050:Declare types in namespaces", Justification = "The scan has to walk a type that has no namespace, so this probe is declared in none.")]
public sealed class GlobalNamespaceHolder
{
    public object? Payload { get; set; }
}
