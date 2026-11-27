using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;
using static Struo.Tests.Api.RelationWriteHarness;

namespace Struo.Tests.Api;

// A key the server never writes (a ReadOnly foreign key, the audit key CreatedBy, a ReadOnly Image field) links
// nothing when a body names it: no read on its target is needed, no id is looked up, and the stored value does
// not move. A foreign key that is also a writable field is a link write like any other.
[Collection("ApiIntegration")]
public sealed class RelationWriteServerOwnedKeyTests : IClassFixture<NoPublicRelationsFixture>
{
    private const string Ledger = "fxLedger";
    private const string Glossary = "fxGlossary";
    private const string PartnerId = "partnerId";

    private static readonly string[] AllTargets = [Ledger, Glossary, "fxTag", "file"];

    private readonly NoPublicRelationsApiFactory factory;
    private readonly RelationWriteHarness h;

    public RelationWriteServerOwnedKeyTests(NoPublicRelationsFixture fixture)
    {
        factory = fixture.Factory;
        h = new(factory);
    }

    private sealed record Seeded(Guid Glossary, Guid Tag, Guid File)
    {
        public LedgerKeys Keys => new(Glossary, null, File, Tag);

        public Guid TargetOf(string key) => key.ToUpperInvariant() switch
        {
            "OWNERID" => Glossary,
            "CREATEDBY" => Tag,
            _ => File,
        };
    }

    private async Task<Seeded> SeedTargetsAsync() =>
        new(await h.SeedGlossaryAsync(), await h.SeedTagAsync(), await h.SeedFileAsync());

    private Task<HttpClient> CallerAsync(bool readsTargets) =>
        h.CallerAsync(readsTargets ? AllTargets : [Ledger], [Ledger]);

    public static TheoryData<string, bool> OwnedKeys()
    {
        var data = new TheoryData<string, bool>();
        foreach (var key in new[] { "ownerId", "OWNERID", "createdBy", "CREATEDBY", "stampId", "STAMPID" })
        {
            data.Add(key, false);
            data.Add(key, true);
        }
        return data;
    }

    private static Dictionary<string, object?> OwnedBody(Seeded t, string missing) => new()
    {
        ["ownerId"] = t.Glossary.ToString(), ["OWNERID"] = missing,
        ["createdBy"] = missing, ["CREATEDBY"] = t.Tag.ToString(),
        ["stampId"] = t.File.ToString(), ["STAMPID"] = missing,
    };

    private static Dictionary<string, object?> WithName(Dictionary<string, object?> fields) =>
        new(fields) { ["name"] = $"c-{Guid.NewGuid():N}" };

    private static void AssertAlike(IReadOnlyList<WriteOutcome> outcomes)
    {
        outcomes.Should().AllSatisfy(AssertWritten);
        outcomes.Select(o => (o.Status, o.Code, o.Message)).Distinct()
            .Should().ContainSingle("the answer must not tell the ids apart");
    }

    [Theory]
    [MemberData(nameof(OwnedKeys))]
    public async Task A_create_that_names_a_server_owned_key_is_accepted_and_does_not_store_it(string key, bool readsTargets)
    {
        var (client, caller) = await h.CallerWithIdAsync(readsTargets ? AllTargets : [Ledger], [Ledger]);
        var targets = await SeedTargetsAsync();
        var outcomes = new List<WriteOutcome>();
        foreach (var kind in IdKinds)
        {
            var fields = new Dictionary<string, object?>
            {
                ["name"] = $"c-{Guid.NewGuid():N}", [key] = IdOf(kind, targets.TargetOf(key)),
            };
            outcomes.Add(await SendAsync(client, Rest, Ledger, null, fields));
        }

        AssertAlike(outcomes);
        foreach (var outcome in outcomes)
        {
            var stored = await h.StoredLedgerAsync(CreatedLedger(outcome));
            (stored.Owner, stored.Partner, stored.Stamp).Should().Be((null, null, null));
            stored.CreatedBy.Should().Be(caller, "the audit stamp is the caller, whatever the body names");
        }
    }

    [Theory]
    [MemberData(nameof(OwnedKeys))]
    public async Task An_update_that_names_a_server_owned_key_is_accepted_and_keeps_the_stored_value(string key, bool readsTargets)
    {
        var client = await CallerAsync(readsTargets);
        var targets = await SeedTargetsAsync();
        var ledger = await h.SeedLedgerAsync(targets.Keys);
        var outcomes = new List<WriteOutcome>();
        foreach (var kind in IdKinds)
            outcomes.Add(await SendAsync(client, Rest, Ledger, ledger, new() { [key] = IdOf(kind, Guid.CreateVersion7()) }));
        outcomes.Add(await SendAsync(client, Rest, Ledger, ledger, new() { [key] = null }));

        AssertAlike(outcomes);
        (await h.StoredLedgerAsync(ledger)).Should().Be(targets.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_body_that_names_every_server_owned_key_twice_in_two_cases_is_accepted(bool readsTargets)
    {
        var client = await CallerAsync(readsTargets);
        var targets = await SeedTargetsAsync();
        var ledger = await h.SeedLedgerAsync(targets.Keys);
        var other = await SeedTargetsAsync();
        var missing = IdOf(Missing, other.Glossary);

        var created = await SendAsync(client, Rest, Ledger, null, WithName(OwnedBody(other, missing)));
        var updated = await SendAsync(client, Rest, Ledger, ledger, OwnedBody(other, missing));

        AssertWritten(created);
        AssertWritten(updated);
        (await h.StoredLedgerAsync(ledger)).Should().Be(targets.Keys);
        var stored = await h.StoredLedgerAsync(CreatedLedger(created));
        (stored.Owner, stored.Stamp).Should().Be((null, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_GraphQL_update_that_names_a_server_owned_key_is_not_refused_for_the_target_and_stores_nothing(
        bool readsTargets)
    {
        var client = await CallerAsync(readsTargets);
        var targets = await SeedTargetsAsync();
        var ledger = await h.SeedLedgerAsync(targets.Keys);

        // The GraphQL inputs do not list a ReadOnly or audit key, so the server rejects the variable itself.
        var query = $"mutation($i: FxLedgerUpdateInput!) {{ updateFxLedger(id: \"{ledger}\", input: $i) {{ id }} }}";
        using var response = await client.PostAsJsonAsync(
            "/graphql", new { query, variables = new { i = new { ownerId = IdOf(Missing, targets.Glossary) } } });
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("\"errors\"").And.NotContain("FORBIDDEN").And.Contain("ownerId");
        (await h.StoredLedgerAsync(ledger)).Should().Be(targets.Keys);
    }

    [Theory]
    [InlineData(Rest, false)]
    [InlineData(Rest, true)]
    [InlineData(Gql, false)]
    [InlineData(Gql, true)]
    public async Task A_revert_does_not_write_a_server_owned_key(string channel, bool readsTargets)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var ledger = await CreateLedgerAsync(admin);
        var targets = await SeedTargetsAsync();
        await h.SetLedgerKeysAsync(ledger, targets.Keys);
        var client = await CallerAsync(readsTargets);
        var before = await h.StoredLedgerAsync(ledger);

        var outcome = await RevertLedgerAsync(client, channel, ledger, RevisionOne);

        outcome.Code.Should().BeNull("a revert never names a key the server does not write, so it needs no read on its target: " + outcome.Body);
        if (channel == Rest) outcome.Status.Should().Be(HttpStatusCode.OK);
        before.Should().Be(targets.Keys);
        (await h.StoredLedgerAsync(ledger)).Should().Be(before);
    }

    [Theory]
    [InlineData(Rest, Post)]
    [InlineData(Rest, Put)]
    [InlineData(Gql, Post)]
    [InlineData(Gql, Put)]
    public async Task A_writable_foreign_key_declared_as_a_text_field_needs_read_on_its_target(string channel, string op)
    {
        var client = await CallerAsync(readsTargets: false);
        var glossary = await h.SeedGlossaryAsync();
        var ledger = await h.SeedLedgerAsync(new(null, null, null, null));
        var outcomes = new List<WriteOutcome>();
        foreach (var kind in IdKinds)
        {
            var fields = new Dictionary<string, object?> { [PartnerId] = IdOf(kind, glossary) };
            if (op == Post) fields["name"] = $"p-{Guid.NewGuid():N}";
            outcomes.Add(await SendAsync(client, channel, Ledger, op == Put ? ledger : null, fields));
        }

        AssertRefusedAlike(outcomes, channel, Glossary);
        (await h.StoredLedgerAsync(ledger)).Partner.Should().BeNull();
    }

    [Theory]
    [InlineData(Rest, Post)]
    [InlineData(Rest, Put)]
    [InlineData(Gql, Post)]
    [InlineData(Gql, Put)]
    public async Task A_writable_foreign_key_declared_as_a_text_field_links_an_existing_row_and_rejects_a_missing_one(
        string channel, string op)
    {
        var client = await CallerAsync(readsTargets: true);
        var glossary = await h.SeedGlossaryAsync();
        var ledger = await h.SeedLedgerAsync(new(null, null, null, null));
        Guid? id = op == Put ? ledger : null;
        var linked = new Dictionary<string, object?> { [PartnerId] = glossary.ToString() };
        var missing = new Dictionary<string, object?> { [PartnerId] = IdOf(Missing, glossary) };
        var name = $"l-{Guid.NewGuid():N}";
        if (op == Post)
        {
            linked["name"] = name;
            missing["name"] = $"m-{Guid.NewGuid():N}";
        }

        var refused = await SendAsync(client, channel, Ledger, id, missing);
        var written = await SendAsync(client, channel, Ledger, id, linked);

        refused.Code.Should().Be("BAD_USER_INPUT", refused.Body);
        refused.Message.Should().Be("The id in 'partnerId' does not exist in 'fxGlossary'.");
        AssertWritten(written);
        if (op == Put) (await h.StoredLedgerAsync(ledger)).Partner.Should().Be(glossary);
        else (await h.StoredLedgerByNameAsync(name)).Partner.Should().Be(glossary);
    }
}
