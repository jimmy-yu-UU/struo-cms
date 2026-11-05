using System.Text.Json;
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Revisions;
using Struo.Infrastructure.Security;
using Struo.Tests.Support;
using Struo.Tests.Support.Seeding;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// Email, Url and top-level Select/Radio values are format-checked on create, update, a partial bind
/// and inside Repeater rows; null and empty skip the check.
/// </summary>
public sealed class ItemServiceFieldFormatTests : IDisposable
{
    private const string Collection = "formatthing";

    public sealed class Contact
    {
        [CmsField(Label = "Name", Interface = FieldInterface.Text)]
        public string? Name { get; set; }

        [CmsField(Label = "Email", Interface = FieldInterface.Email)]
        public string? Email { get; set; }

        [CmsField(Label = "Site", Interface = FieldInterface.Url)]
        public string? Site { get; set; }
    }

    [SugarTable("format_thing")]
    [CmsCollection("FormatThing")]
    public sealed class FormatThing : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [CmsField(Label = "Must Email", Interface = FieldInterface.Email, Required = true)]
        public string MustEmail { get; set; } = "";

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Email", Interface = FieldInterface.Email)]
        public string? Email { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Site", Interface = FieldInterface.Url)]
        public string? Site { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Status", Interface = FieldInterface.Select)]
        [CmsOptions("draft:Draft", "published:Published")]
        public string? Status { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Mode", Interface = FieldInterface.Radio)]
        [CmsOptions("light:Light", "dark:Dark")]
        public string? Mode { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Free", Interface = FieldInterface.Select)]
        public string? Free { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Note", Interface = FieldInterface.Text)]
        public string? Note { get; set; }

        [CmsField(Label = "Contacts", Interface = FieldInterface.Repeater)]
        public List<Contact> Contacts { get; set; } = new();
    }

    private readonly ITestDatabase _file = TestBackend.CreateUnitDatabase();
    private readonly ISqlSugarClient _db;
    private readonly ItemService _svc;
    private readonly ItemDeserializer _deserializer;
    private readonly CollectionMetadata _meta;

    public ItemServiceFieldFormatTests()
    {
        _db = SqlSugarClientFactory.Create(_file.Options(), new TestCurrentUserAccessor(Guid.Empty));
        _db.CodeFirst.InitTables<FormatThing>();
        _db.CodeFirst.InitTables<Language>();
        _db.CodeFirst.InitTables<Revision>();
        LanguageSeeder.SeedAsync(_db, TestLocalization.Default).GetAwaiter().GetResult();

        var types = new[] { typeof(FormatThing) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            [Collection] = typeof(FormatThing),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(_db, registry, graph, provider, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph, new StruoQueryOptions());
        var languages = new LanguageProvider(_db);
        var revisionUser = new TestCurrentUserAccessor(Guid.Empty);
        var revisionStore = new SqlSugarRevisionStore(_db, revisionUser);
        var snapshotBuilder = new RevisionSnapshotBuilder(repo, provider, registry, graph);
        _svc = TestItemService.Create(
            repo, provider, registry, graph, expander, graph, languages,
            revisionUser, revisionStore, snapshotBuilder);
        _deserializer = new ItemDeserializer(registry, graph, new RichTextCleaner(new GanssHtmlSanitizer()));
        _meta = provider.GetCollection(Collection)!;
    }

    public void Dispose() => _file.Dispose();

    private static JsonElement Body(Dictionary<string, object?> fields)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(fields));
        return doc.RootElement.Clone();
    }

    private static Dictionary<string, object?> With(string key, object? value) =>
        new() { ["mustEmail"] = "must@example.com", [key] = value };

    private Task<IReadOnlyDictionary<string, object?>> CreateWith(string key, object? value) =>
        _svc.CreateAsync(Collection, Body(With(key, value)));

    // Email

    [Theory]
    [InlineData("plain")]
    [InlineData("a@b")]
    [InlineData("a@@b.c")]
    [InlineData("a@b@c.d")]
    [InlineData("@b.c")]
    [InlineData("a@.c")]
    [InlineData("a@b.")]
    [InlineData("a b@c.d")]
    [InlineData("a@b.c ")]
    [InlineData(" a@b.c")]
    [InlineData("a@b\t.c")]
    public async Task Invalid_email_is_rejected_on_create(string value)
    {
        var act = () => CreateWith("email", value);
        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'email' must be a valid email address.");
    }

    [Theory]
    [InlineData("a@b.co")]
    [InlineData("first.last+tag@sub.example.com")]
    public async Task Valid_email_is_accepted_on_create(string value)
    {
        var created = await CreateWith("email", value);
        created["email"].Should().Be(value);
    }

    [Fact]
    public async Task Null_and_empty_email_skip_the_check()
    {
        (await CreateWith("email", null))["email"].Should().BeNull();
        (await CreateWith("email", ""))["email"].Should().Be("");
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task Whitespace_only_email_is_a_format_error_when_not_required(string value)
    {
        var act = () => CreateWith("email", value);
        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'email' must be a valid email address.");
    }

    [Fact]
    public async Task Whitespace_only_required_email_reports_required_not_format()
    {
        var act = () => _svc.CreateAsync(Collection, Body(new() { ["mustEmail"] = "  " }));
        await act.Should().ThrowAsync<QueryException>().WithMessage("Field 'mustEmail' is required.");
    }

    [Fact]
    public async Task Invalid_required_email_is_rejected_with_the_format_message()
    {
        var act = () => _svc.CreateAsync(Collection, Body(new() { ["mustEmail"] = "nope" }));
        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'mustEmail' must be a valid email address.");
    }

    // Url

    [Theory]
    [InlineData("x")]
    [InlineData("/relative/path")]
    [InlineData("www.example.com")]
    [InlineData("ftp://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:a@b.co")]
    [InlineData("http://")]
    [InlineData("http://example.com/a b")]
    [InlineData("  http://x  ")]
    [InlineData("http://x\n")]
    public async Task Invalid_url_is_rejected_on_create(string value)
    {
        var act = () => CreateWith("site", value);
        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'site' must be a valid http or https URL.");
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com/a/b?c=1#d")]
    [InlineData("HTTPS://Example.com")]
    public async Task Valid_url_is_accepted_on_create(string value)
    {
        var created = await CreateWith("site", value);
        created["site"].Should().Be(value);
    }

    [Fact]
    public async Task Null_and_empty_url_skip_the_check()
    {
        (await CreateWith("site", null))["site"].Should().BeNull();
        (await CreateWith("site", ""))["site"].Should().Be("");
    }

    // Select / Radio

    [Fact]
    public async Task Select_value_outside_its_options_is_rejected()
    {
        var act = () => CreateWith("status", "archived");
        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'status' value 'archived' is not in its options.");
    }

    [Fact]
    public async Task Select_option_match_is_ordinal()
    {
        var act = () => CreateWith("status", "Draft");
        await act.Should().ThrowAsync<QueryException>().WithMessage("*not in its options*");
    }

    [Fact]
    public async Task Select_value_inside_its_options_is_accepted()
    {
        (await CreateWith("status", "published"))["status"].Should().Be("published");
    }

    [Fact]
    public async Task Radio_value_outside_its_options_is_rejected()
    {
        var act = () => CreateWith("mode", "blue");
        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'mode' value 'blue' is not in its options.");
    }

    [Fact]
    public async Task Radio_value_inside_its_options_is_accepted()
    {
        (await CreateWith("mode", "dark"))["mode"].Should().Be("dark");
    }

    [Fact]
    public async Task Null_and_empty_select_skip_the_check()
    {
        (await CreateWith("status", null))["status"].Should().BeNull();
        (await CreateWith("status", ""))["status"].Should().Be("");
    }

    [Fact]
    public async Task Select_without_options_checks_nothing()
    {
        (await CreateWith("free", "anything"))["free"].Should().Be("anything");
    }

    // Update and partial bind

    private async Task<string> CreateValidAsync()
    {
        var created = await CreateWith("note", "n");
        return created["id"]!.ToString()!;
    }

    [Fact]
    public async Task Update_with_an_invalid_value_is_rejected_and_writes_nothing()
    {
        var id = await CreateValidAsync();

        var act = () => _svc.UpdateAsync(Collection, id, Body(new() { ["email"] = "bad", ["note"] = "changed" }));

        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'email' must be a valid email address.");
        var read = await _svc.GetAsync(Collection, id);
        read!["note"].Should().Be("n");
    }

    [Fact]
    public async Task Update_with_a_valid_value_succeeds()
    {
        var id = await CreateValidAsync();
        var updated = await _svc.UpdateAsync(Collection, id,
            Body(new() { ["site"] = "https://example.com", ["status"] = "draft" }));
        updated!["site"].Should().Be("https://example.com");
    }

    [Fact]
    public async Task Update_skips_a_field_the_body_does_not_send()
    {
        var id = Guid.NewGuid();
        await _db.Insertable(new FormatThing
        {
            Id = id, MustEmail = "must@example.com", Email = "stored-bad", Site = "x", Status = "gone",
        }).ExecuteCommandAsync();

        var updated = await _svc.UpdateAsync(Collection, id.ToString(), Body(new() { ["note"] = "edited" }));

        updated!["note"].Should().Be("edited");
        updated["email"].Should().Be("stored-bad");
    }

    [Fact]
    public async Task Update_that_sends_the_stored_bad_value_again_is_rejected()
    {
        var id = Guid.NewGuid();
        await _db.Insertable(new FormatThing { Id = id, MustEmail = "must@example.com", Status = "gone" })
            .ExecuteCommandAsync();

        var act = () => _svc.UpdateAsync(Collection, id.ToString(), Body(new() { ["status"] = "gone" }));

        await act.Should().ThrowAsync<QueryException>().WithMessage("*'gone' is not in its options*");
    }

    private static HashSet<string> Only(params string[] names) =>
        new(names, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Partial_bind_rejects_an_invalid_value_it_was_asked_for()
    {
        var act = () => _deserializer.DeserializePartial(
            Collection, Body(new() { ["email"] = "bad", ["site"] = "x" }), _meta, Only("email"));

        act.Should().Throw<QueryException>().WithMessage("Field 'email' must be a valid email address.");
    }

    [Fact]
    public void Partial_bind_returns_valid_values_and_ignores_fields_outside_onlyFields()
    {
        var result = _deserializer.DeserializePartial(
            Collection, Body(new() { ["email"] = "a@b.co", ["site"] = "x" }), _meta, Only("email"));

        result["Email"].Should().Be("a@b.co");
        result.Should().NotContainKey("Site");
    }

    [Fact]
    public void Partial_bind_skips_an_absent_field()
    {
        var result = _deserializer.DeserializePartial(
            Collection, Body(new() { ["note"] = "x" }), _meta, Only("email", "status", "note"));

        result.Keys.Should().BeEquivalentTo(["Note"]);
    }

    [Fact]
    public void Partial_bind_rejects_a_select_outside_its_options()
    {
        var act = () => _deserializer.DeserializePartial(
            Collection, Body(new() { ["status"] = "nope" }), _meta, Only("status"));

        act.Should().Throw<QueryException>().WithMessage("*'nope' is not in its options*");
    }

    // Repeater rows

    private static object[] Rows(params object[] rows) => rows;

    [Fact]
    public async Task Repeater_row_with_an_invalid_email_is_rejected()
    {
        var body = Body(With("contacts", Rows(
            new { name = "a", email = "a@b.co", site = (string?)null },
            new { name = "b", email = "bad", site = (string?)null })));

        var act = () => _svc.CreateAsync(Collection, body);

        await act.Should().ThrowAsync<QueryException>().WithMessage(
            "Repeater field 'contacts' row 2: 'email' must be a valid email address.");
    }

    [Fact]
    public async Task Repeater_row_with_a_whitespace_only_email_is_a_format_error()
    {
        var body = Body(With("contacts", Rows(new { name = "a", email = "  ", site = (string?)null })));

        var act = () => _svc.CreateAsync(Collection, body);

        await act.Should().ThrowAsync<QueryException>().WithMessage(
            "Repeater field 'contacts' row 1: 'email' must be a valid email address.");
    }

    [Fact]
    public async Task Repeater_row_with_an_invalid_url_is_rejected()
    {
        var body = Body(With("contacts", Rows(new { name = "a", email = (string?)null, site = "x" })));

        var act = () => _svc.CreateAsync(Collection, body);

        await act.Should().ThrowAsync<QueryException>().WithMessage(
            "Repeater field 'contacts' row 1: 'site' must be a valid http or https URL.");
    }

    [Fact]
    public async Task Repeater_row_with_valid_or_empty_values_is_accepted()
    {
        var body = Body(With("contacts", Rows(
            new { name = "a", email = "a@b.co", site = "https://example.com" },
            new { name = "b", email = "", site = (string?)null })));

        var created = await _svc.CreateAsync(Collection, body);

        ((IEnumerable<Contact>)created["contacts"]!).Should().HaveCount(2);
    }

    [Fact]
    public async Task Repeater_row_validation_runs_on_update()
    {
        var id = await CreateValidAsync();

        var act = () => _svc.UpdateAsync(Collection, id,
            Body(new() { ["contacts"] = Rows(new { name = "a", email = "bad", site = (string?)null }) }));

        await act.Should().ThrowAsync<QueryException>().WithMessage("*row 1: 'email' must be a valid email address*");
    }

    // Precedence: the existing Repeater phase keeps winning over the new one

    [Fact]
    public async Task Repeater_row_error_wins_over_a_root_format_error()
    {
        var fields = With("contacts", Rows(new { name = "a", email = "bad", site = (string?)null }));
        fields["status"] = "archived";

        var act = () => _svc.CreateAsync(Collection, Body(fields));

        await act.Should().ThrowAsync<QueryException>().WithMessage("Repeater field 'contacts'*");
    }
}
