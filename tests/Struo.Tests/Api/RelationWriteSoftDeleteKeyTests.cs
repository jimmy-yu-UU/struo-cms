using System.Net.Http.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;
using static Struo.Tests.Api.RelationWriteHarness;

namespace Struo.Tests.Api;

// DeletedAt and DeletedBy belong to the soft-delete machinery, not to a body: a collection that declares a
// relation on DeletedBy still never stores a value a body names for it, and a caller needs no read on the
// relation target to name it.
[Collection("ApiIntegration")]
public sealed class RelationWriteSoftDeleteKeyTests : IClassFixture<NoPublicRelationsFixture>
{
    private const string Tombstone = "fxTombstone";
    private const string DeletedBy = "deletedBy";

    private static readonly string[] AllTargets = [Tombstone, "fxTag"];

    private readonly RelationWriteHarness h;

    public RelationWriteSoftDeleteKeyTests(NoPublicRelationsFixture fixture) => h = new(fixture.Factory);

    private Task<HttpClient> CallerAsync(bool readsTarget) =>
        h.CallerAsync(readsTarget ? AllTargets : [Tombstone], [Tombstone]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_create_naming_the_soft_delete_key_is_accepted_and_does_not_store_it(bool readsTarget)
    {
        var client = await CallerAsync(readsTarget);
        var tag = await h.SeedTagAsync();
        var fields = new Dictionary<string, object?> { ["name"] = $"t-{Guid.NewGuid():N}", [DeletedBy] = tag.ToString() };

        var outcome = await SendAsync(client, Rest, Tombstone, null, fields);

        AssertWritten(outcome);
        (await h.StoredTombstoneAsync(CreatedTombstone(outcome))).Should().Be(new TombstoneKeys(null, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_update_naming_the_soft_delete_key_is_accepted_and_does_not_store_it(bool readsTarget)
    {
        var client = await CallerAsync(readsTarget);
        var tag = await h.SeedTagAsync();
        var tombstone = await h.SeedTombstoneAsync();

        var outcome = await SendAsync(client, Rest, Tombstone, tombstone, new() { [DeletedBy] = tag.ToString() });

        AssertWritten(outcome);
        (await h.StoredTombstoneAsync(tombstone)).Should().Be(new TombstoneKeys(null, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_GraphQL_update_naming_the_soft_delete_key_is_not_refused_for_the_target_and_stores_nothing(bool readsTarget)
    {
        var client = await CallerAsync(readsTarget);
        var tag = await h.SeedTagAsync();
        var tombstone = await h.SeedTombstoneAsync();

        // The GraphQL inputs do not list a soft-delete key, so the server rejects the variable itself.
        var query = $"mutation($i: FxTombstoneUpdateInput!) {{ updateFxTombstone(id: \"{tombstone}\", input: $i) {{ id }} }}";
        using var response = await client.PostAsJsonAsync("/graphql", new { query, variables = new { i = new { deletedBy = tag } } });
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("\"errors\"").And.NotContain("FORBIDDEN").And.Contain(DeletedBy);
        (await h.StoredTombstoneAsync(tombstone)).Should().Be(new TombstoneKeys(null, null));
    }
}
