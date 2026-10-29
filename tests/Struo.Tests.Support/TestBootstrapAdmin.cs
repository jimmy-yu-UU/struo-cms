namespace Struo.Tests.Support;

/// <summary>The bootstrap admin password every test host supplies: the core seed refuses to create an
/// admin without a configured one.</summary>
public static class TestBootstrapAdmin
{
    public const string Password = "bootstrap-test-pw-123456";
}
