using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;
using static Struo.Tests.Api.RelationWriteHarness;

namespace Struo.Tests.Api;

// A junction collection can carry a ReadOnly key when the key is also the foreign key of a many-to-one
// relation. A body never sets that key, so naming it links nothing, needs no read on its target and is not
// looked up; the stored value does not move.
[Collection("ApiIntegration")]
public sealed class RelationWriteServerOwnedJunctionKeyTests : IClassFixture<NoPublicRelationsFixture>
{
    private const string BinderLink = "fxBinderLink";

    private readonly RelationWriteHarness h;

    public RelationWriteServerOwnedJunctionKeyTests(NoPublicRelationsFixture fixture) => h = new(fixture.Factory);

    private Task<HttpClient> CallerAsync(bool readsTargets) =>
        h.CallerAsync(readsTargets ? [BinderLink, "fxBinder", "fxTag"] : [BinderLink], [BinderLink]);

    [Theory]
    [InlineData(Rest, false)]
    [InlineData(Rest, true)]
    [InlineData(Gql, false)]
    [InlineData(Gql, true)]
    public async Task An_update_naming_the_read_only_key_is_accepted_and_keeps_the_stored_value(string channel, bool readsTargets)
    {
        var client = await CallerAsync(readsTargets);
        var binder = await h.SeedBinderAsync();
        var other = await h.SeedBinderAsync();
        var tag = await h.SeedTagAsync();
        var link = await h.SeedBinderLinkAsync(binder, tag);

        // GraphQL has no input field for the key, so only REST names it.
        if (channel == Gql)
        {
            AssertWritten(await SendAsync(client, channel, BinderLink, link, new() { ["tagId"] = tag.ToString() }));
        }
        else
        {
            var outcomes = new List<WriteOutcome>();
            foreach (var kind in IdKinds)
                outcomes.Add(await SendAsync(client, channel, BinderLink, link, new() { ["binderId"] = IdOf(kind, other) }));
            outcomes.Should().AllSatisfy(AssertWritten);
            outcomes.Select(o => (o.Status, o.Code, o.Message)).Distinct().Should().ContainSingle();
        }

        var stored = await h.BinderLinkAsync(link);
        (stored.BinderId, stored.TagId).Should().Be((binder, tag));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_create_naming_the_read_only_key_is_accepted_and_does_not_store_it(bool readsTargets)
    {
        var client = await CallerAsync(readsTargets);
        var binder = await h.SeedBinderAsync();

        var outcome = await SendAsync(client, Rest, BinderLink, null, new() { ["binderId"] = binder.ToString() });

        AssertWritten(outcome);
        (await h.BinderLinkAsync(CreatedLedger(outcome))).BinderId.Should().Be(Guid.Empty);
    }
}
