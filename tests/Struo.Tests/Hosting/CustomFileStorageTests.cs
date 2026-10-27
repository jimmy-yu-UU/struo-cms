// tests/Struo.Tests/Hosting/CustomFileStorageTests.cs
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Files;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Hosting;

/// <summary>
/// <c>Struo:Files:Backend=custom</c> builds no storage of its own: the fork registers its
/// <see cref="IFileStorage"/> after <c>AddStruoCms()</c>, and a host without one fails at startup.
/// </summary>
[Collection("ApiIntegration")]
public class CustomFileStorageTests
{
    private sealed class ForkStorage : IFileStorage
    {
        public Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
        public IAsyncEnumerable<StoredBlob> ListAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public bool SupportsPresignedUrls => false;
        public Task<string?> GetPresignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public async Task The_custom_backend_uses_the_storage_the_fork_registered()
    {
        var fork = new ForkStorage();

        await using var host = await ForkHost.StartAsync(
            mutate: s => s["Struo:Files:Backend"] = "custom",
            afterAddStruoCms: services => services.AddSingleton<IFileStorage>(fork));

        host.App.Services.GetRequiredService<IFileStorage>().Should().BeSameAs(fork);
    }

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public async Task The_custom_backend_without_a_registered_storage_fails_startup()
    {
        var error = await ForkHost.StartupErrorAsync(mutate: s => s["Struo:Files:Backend"] = "custom");

        error.Should().NotBeNull();
        error!.ToString().Should().Contain("Struo:Files:Backend").And.Contain("IFileStorage");
    }

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public async Task A_storage_registered_after_AddStruoCms_replaces_the_local_default()
    {
        var fork = new ForkStorage();

        await using var host = await ForkHost.StartAsync(
            afterAddStruoCms: services => services.AddSingleton<IFileStorage>(fork));

        host.App.Services.GetRequiredService<IFileStorage>().Should().BeSameAs(fork);
    }
}
