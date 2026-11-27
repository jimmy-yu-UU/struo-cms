using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;
using static Struo.Tests.Api.RelationWriteHarness;

namespace Struo.Tests.Api;

// A Hidden field is not returned by the item read, so a caller who cannot read files gets no unchanged-value
// exemption for it: sending it is refused whatever the value, stored or not. GraphQL takes no Hidden field in
// its inputs, so only REST can send the key.
[Collection("ApiIntegration")]
public sealed class RelationWriteHiddenFileTests : IClassFixture<NoPublicRelationsFixture>
{
    private const string Vault = "fxVault";
    private const string SecretImage = "secretImageId";
    private const string SecretGallery = "secretGallery";
    private const string SecretCover = "secretCoverId";

    private readonly NoPublicRelationsApiFactory factory;
    private readonly RelationWriteHarness h;

    public RelationWriteHiddenFileTests(NoPublicRelationsFixture fixture)
    {
        factory = fixture.Factory;
        h = new(factory);
    }

    private async Task<(Guid Vault, Guid File)> SeedVaultAsync()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var file = await h.SeedFileAsync();
        using var resp = await admin.PostAsJsonAsync($"/api/items/{Vault}", new
        {
            name = "vault",
            secretImageId = file,
            secretGallery = new[] { file },
            translations = new { en = new { title = "t", secretCoverId = file } },
        });
        resp.EnsureSuccessStatusCode();
        var root = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return (Guid.Parse(root.GetProperty("data").GetProperty("id").GetString()!), file);
    }

    [Theory]
    [InlineData(SecretImage, true)]
    [InlineData(SecretImage, false)]
    [InlineData(SecretGallery, true)]
    [InlineData(SecretGallery, false)]
    [InlineData(SecretCover, true)]
    [InlineData(SecretCover, false)]
    public async Task A_hidden_file_field_is_refused_whatever_the_value(string field, bool storedValue)
    {
        var (vault, file) = await SeedVaultAsync();
        var client = await h.CallerAsync([Vault], [Vault]);
        object? value = field == SecretGallery
            ? (storedValue ? new[] { file.ToString() } : Array.Empty<string>())
            : (storedValue ? file.ToString() : null);
        var translated = field == SecretCover;

        var outcome = await SendAsync(
            client, Rest, Vault, vault, translated ? new() : new() { [field] = value },
            translation: translated ? new() { [field] = value } : null);

        AssertRefusedAlike([outcome], Rest, "file");
    }

    [Fact]
    public async Task A_caller_with_file_read_may_send_a_hidden_file_field()
    {
        var (vault, file) = await SeedVaultAsync();
        var client = await h.CallerAsync([Vault, "file"], [Vault]);

        var outcome = await SendAsync(client, Rest, Vault, vault, new() { [SecretImage] = file.ToString() });

        AssertWritten(outcome);
    }
}
