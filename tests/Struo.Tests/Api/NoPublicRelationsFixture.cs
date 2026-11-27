using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>A host where neither the <c>file</c> nor the <c>fxCategory</c> collection is publicly readable.</summary>
public sealed class NoPublicRelationsApiFactory : NoPublicFileApiFactory
{
    protected override IReadOnlyList<string> PublicReadCollections => ["fxArticle"];
}

/// <summary>One <see cref="NoPublicRelationsApiFactory"/> host for a test class, with the public read grant on <c>file</c> removed.</summary>
public sealed class NoPublicRelationsFixture : IAsyncLifetime
{
    public NoPublicRelationsApiFactory Factory { get; } = new();

    public Task InitializeAsync() => Factory.RevokePublicFileReadAsync();

    public Task DisposeAsync()
    {
        Factory.Dispose();
        return Task.CompletedTask;
    }
}
