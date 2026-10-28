// tests/Struo.Tests/Support/ApiFactory.cs
namespace Struo.Tests.Support;

/// <summary>The shared API host of this suite: <see cref="StruoApiFactory"/> plus the fixtures' public collections.</summary>
public sealed class ApiFactory : StruoApiFactory
{
    protected override IReadOnlyList<string> PublicReadCollections => ["fxArticle", "fxCategory"];
}
