using System.Net;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;
using static Struo.Tests.Api.RelationWriteHarness;

namespace Struo.Tests.Api;

// A foreign key that is also a declared, visible field (fxLedger.partnerId) is returned by an item read, so a
// client that sends the item back names it. On an update the key sent with the value the row stores links nothing
// new and needs no read on its target; any other value, every create and every spelling that differs are refused.
[Collection("ApiIntegration")]
public sealed class RelationWriteDeclaredKeyUnchangedTests : IClassFixture<NoPublicRelationsFixture>
{
    private const string Ledger = "fxLedger";
    private const string Glossary = "fxGlossary";

    private static readonly string[] AllTargets = [Ledger, Glossary, "fxTag", "file"];

    private readonly NoPublicRelationsApiFactory factory;
    private readonly RelationWriteHarness h;

    public RelationWriteDeclaredKeyUnchangedTests(NoPublicRelationsFixture fixture)
    {
        factory = fixture.Factory;
        h = new(factory);
    }

    private Task<HttpClient> CallerAsync(bool readsTarget) =>
        h.CallerAsync(readsTarget ? AllTargets : [Ledger], [Ledger]);

    private Task<Guid> LedgerWithPartnerAsync(Guid? partner) => h.SeedLedgerAsync(new(null, partner, null, null));

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task The_stored_value_sent_back_is_accepted_without_read_on_the_target(string channel)
    {
        var client = await CallerAsync(readsTarget: false);
        var partner = await h.SeedGlossaryAsync();
        var ledger = await LedgerWithPartnerAsync(partner);

        var outcome = await SendAsync(client, channel, Ledger, ledger, new() { ["partnerId"] = partner.ToString() });

        AssertWritten(outcome);
        (await h.StoredLedgerAsync(ledger)).Should().Be(new LedgerKeys(null, partner, null, null));
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task A_stored_null_sent_as_null_is_accepted_without_read_on_the_target(string channel)
    {
        var client = await CallerAsync(readsTarget: false);
        var ledger = await LedgerWithPartnerAsync(null);

        AssertWritten(await SendAsync(client, channel, Ledger, ledger, new() { ["partnerId"] = null }));

        (await h.StoredLedgerAsync(ledger)).Partner.Should().BeNull();
    }

    [Fact]
    public async Task The_id_text_may_differ_in_case_from_the_stored_one()
    {
        var client = await CallerAsync(readsTarget: false);
        var partner = await h.SeedGlossaryAsync();
        var ledger = await LedgerWithPartnerAsync(partner);

        var outcome = await SendAsync(
            client, Rest, Ledger, ledger, new() { ["PartnerId"] = partner.ToString().ToUpperInvariant() });

        AssertWritten(outcome);
        (await h.StoredLedgerAsync(ledger)).Partner.Should().Be(partner);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task A_changed_value_is_refused_the_same_for_an_existing_a_missing_and_a_malformed_id(string channel)
    {
        var client = await CallerAsync(readsTarget: false);
        var partner = await h.SeedGlossaryAsync();
        var other = await h.SeedGlossaryAsync();
        var ledger = await LedgerWithPartnerAsync(partner);
        var outcomes = new List<WriteOutcome>();
        foreach (var kind in IdKinds)
            outcomes.Add(await SendAsync(client, channel, Ledger, ledger, new() { ["partnerId"] = IdOf(kind, other) }));
        outcomes.Add(await SendAsync(client, channel, Ledger, ledger, new() { ["partnerId"] = null }));

        AssertRefusedAlike(outcomes, channel, Glossary);
        (await h.StoredLedgerAsync(ledger)).Partner.Should().Be(partner);
    }

    [Fact]
    public async Task A_value_sent_for_a_stored_null_is_refused_the_same_for_every_kind_of_id()
    {
        var client = await CallerAsync(readsTarget: false);
        var partner = await h.SeedGlossaryAsync();
        var ledger = await LedgerWithPartnerAsync(null);
        var outcomes = new List<WriteOutcome>();
        foreach (var kind in IdKinds)
            outcomes.Add(await SendAsync(client, Rest, Ledger, ledger, new() { ["partnerId"] = IdOf(kind, partner) }));

        AssertRefusedAlike(outcomes, Rest, Glossary);
        (await h.StoredLedgerAsync(ledger)).Partner.Should().BeNull();
    }

    [Fact]
    public async Task Every_spelling_of_the_key_must_hold_the_stored_value()
    {
        var client = await CallerAsync(readsTarget: false);
        var partner = await h.SeedGlossaryAsync();
        var other = await h.SeedGlossaryAsync();
        var ledger = await LedgerWithPartnerAsync(partner);
        var outcomes = new List<WriteOutcome>();
        foreach (var kind in IdKinds)
        {
            var stored = partner.ToString();
            var changed = IdOf(kind, other);
            outcomes.Add(await SendAsync(client, Rest, Ledger, ledger, new() { ["partnerId"] = stored, ["PARTNERID"] = changed }));
            outcomes.Add(await SendAsync(client, Rest, Ledger, ledger, new() { ["PARTNERID"] = changed, ["partnerId"] = stored }));
        }

        AssertRefusedAlike(outcomes, Rest, Glossary);
        (await h.StoredLedgerAsync(ledger)).Partner.Should().Be(partner);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task A_create_is_refused_whatever_the_value(string channel)
    {
        var client = await CallerAsync(readsTarget: false);
        var partner = await h.SeedGlossaryAsync();
        var outcomes = new List<WriteOutcome>();
        foreach (var kind in IdKinds)
        {
            var fields = new Dictionary<string, object?> { ["name"] = $"c-{Guid.NewGuid():N}", ["partnerId"] = IdOf(kind, partner) };
            outcomes.Add(await SendAsync(client, channel, Ledger, null, fields));
        }
        outcomes.Add(await SendAsync(client, channel, Ledger, null, new() { ["name"] = $"c-{Guid.NewGuid():N}", ["partnerId"] = null }));

        AssertRefusedAlike(outcomes, channel, Glossary);
    }

    [Fact]
    public async Task A_missing_item_answers_not_found_before_the_key_is_considered()
    {
        var client = await CallerAsync(readsTarget: false);
        var partner = await h.SeedGlossaryAsync();

        var outcome = await SendAsync(client, Rest, Ledger, Guid.NewGuid(), new() { ["partnerId"] = partner.ToString() });

        outcome.Status.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task A_revert_does_not_write_the_key_for_a_caller_without_read_on_the_target(string channel)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var ledger = await CreateLedgerAsync(admin);
        var partner = await h.SeedGlossaryAsync();
        await h.SetLedgerKeysAsync(ledger, new(null, partner, null, null));
        var client = await CallerAsync(readsTarget: false);

        var outcome = await RevertLedgerAsync(client, channel, ledger, RevisionOne);

        AssertWritten(outcome);
        (await h.StoredLedgerAsync(ledger)).Partner.Should().Be(partner, "the revert leaves out a key it cannot link");
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task A_revert_writes_the_snapshot_value_for_a_caller_who_reads_the_target(string channel)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var ledger = await CreateLedgerAsync(admin);
        var partner = await h.SeedGlossaryAsync();
        await h.SetLedgerKeysAsync(ledger, new(null, partner, null, null));
        var client = await CallerAsync(readsTarget: true);

        AssertWritten(await RevertLedgerAsync(client, channel, ledger, RevisionOne));

        (await h.StoredLedgerAsync(ledger)).Partner.Should().BeNull();
    }
}
