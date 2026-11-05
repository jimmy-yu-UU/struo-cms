using System.Text.Json;
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
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
/// Translatable Email, Url, Select and Radio values follow the same rules as root fields, and the
/// error names the field and the locale.
/// </summary>
public sealed class ItemServiceFieldFormatTranslationTests : IDisposable
{
    private const string Collection = "formatpage";

    [SugarTable("format_page_translations")]
    public sealed class FormatPageTranslation
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
        public Guid FormatPageId { get; set; }
        public string Locale { get; set; } = "";

        [CmsField(Label = "Title", Interface = FieldInterface.Text, Required = true, MaxLength = 12)]
        public string Title { get; set; } = "";

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Email", Interface = FieldInterface.Email, MaxLength = 20)]
        public string? Email { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Site", Interface = FieldInterface.Url)]
        public string? Site { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Pick", Interface = FieldInterface.Select)]
        [CmsOptions("a:A", "b:B")]
        public string? Pick { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Mode", Interface = FieldInterface.Radio)]
        [CmsOptions("light:Light", "dark:Dark")]
        public string? Mode { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Free", Interface = FieldInterface.Select)]
        public string? Free { get; set; }
    }

    [SugarTable("format_page")]
    [CmsCollection("FormatPage")]
    public sealed class FormatPage : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [CmsTranslations(typeof(FormatPageTranslation))]
        [SugarColumn(IsIgnore = true)]
        public List<FormatPageTranslation> Translations { get; set; } = [];
    }

    private readonly ITestDatabase _file = TestBackend.CreateUnitDatabase();
    private readonly ItemService _svc;

    public ItemServiceFieldFormatTranslationTests()
    {
        var db = SqlSugarClientFactory.Create(_file.Options(), new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<FormatPage>();
        db.CodeFirst.InitTables<FormatPageTranslation>();
        db.CodeFirst.InitTables<Language>();
        db.CodeFirst.InitTables<Revision>();
        LanguageSeeder.SeedAsync(db, TestLocalization.Default).GetAwaiter().GetResult();

        var types = new[] { typeof(FormatPage), typeof(FormatPageTranslation) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            [Collection] = typeof(FormatPage),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph, new StruoQueryOptions());
        var languages = new LanguageProvider(db);
        var revisionUser = new TestCurrentUserAccessor(Guid.Empty);
        var revisionStore = new SqlSugarRevisionStore(db, revisionUser);
        var snapshotBuilder = new RevisionSnapshotBuilder(repo, provider, registry, graph);
        _svc = TestItemService.Create(
            repo, provider, registry, graph, expander, graph, languages,
            revisionUser, revisionStore, snapshotBuilder);
    }

    public void Dispose() => _file.Dispose();

    private static JsonElement Body(object translations) =>
        JsonSerializer.SerializeToElement(new { translations });

    private static Dictionary<string, object?> En(string key, object? value) =>
        new() { ["title"] = "T", [key] = value };

    private static async Task<QueryException> Rejected(Task<object?> write)
    {
        try { await write; }
        catch (QueryException ex) { return ex; }
        throw new Xunit.Sdk.XunitException("Expected a QueryException.");
    }

    private async Task<object?> CreateAsync(object translations) =>
        await _svc.CreateAsync(Collection, Body(translations));

    private async Task<string> CreateValidAsync()
    {
        var created = await _svc.CreateAsync(Collection, Body(new { en = new { title = "T" } }));
        return created["id"]!.ToString()!;
    }

    private async Task<object?> UpdateAsync(string id, object translations) =>
        await _svc.UpdateAsync(Collection, id, Body(translations));

    public static TheoryData<string, string, string> Invalid => new()
    {
        { "email", "bad", "Field 'email' must be a valid email address for locale 'zh-TW'." },
        { "email", "a b@c.d", "Field 'email' must be a valid email address for locale 'zh-TW'." },
        { "site", "x", "Field 'site' must be a valid http or https URL for locale 'zh-TW'." },
        { "site", "ftp://example.com", "Field 'site' must be a valid http or https URL for locale 'zh-TW'." },
        { "pick", "z", "Field 'pick' value 'z' is not in its options for locale 'zh-TW'." },
        { "mode", "blue", "Field 'mode' value 'blue' is not in its options for locale 'zh-TW'." },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task Invalid_value_in_a_non_default_locale_is_rejected_with_field_and_locale(
        string field, string value, string message)
    {
        var ex = await Rejected(CreateAsync(new Dictionary<string, object>
        {
            ["en"] = new { title = "T" },
            ["zh-TW"] = En(field, value),
        })!);

        ex.Message.Should().Be(message);
        var detail = ex.Details!.Single();
        detail.Field.Should().Be(field);
        detail.Locale.Should().Be("zh-TW");
        detail.Message.Should().Be(message);
    }

    [Theory]
    [InlineData("email", "a@b.co")]
    [InlineData("site", "https://example.com")]
    [InlineData("pick", "b")]
    [InlineData("mode", "dark")]
    [InlineData("free", "anything")]
    public async Task Valid_value_passes_and_is_stored(string field, string value)
    {
        var created = await _svc.CreateAsync(Collection, Body(new Dictionary<string, object>
        {
            ["en"] = new { title = "T" },
            ["zh-TW"] = En(field, value),
        }));

        var read = await _svc.GetAsync(Collection, created["id"]!.ToString()!);
        var tr = (IReadOnlyDictionary<string, Dictionary<string, object?>>)read!["translations"]!;
        tr["zh-TW"][field].Should().Be(value);
    }

    [Fact]
    public async Task Whitespace_only_value_is_a_format_error_with_field_and_locale()
    {
        var ex = await Rejected(CreateAsync(new Dictionary<string, object>
        {
            ["en"] = new { title = "T" },
            ["zh-TW"] = En("email", "   "),
        })!);

        ex.Message.Should().Be("Field 'email' must be a valid email address for locale 'zh-TW'.");
    }

    [Fact]
    public async Task Null_and_empty_skip_the_check_and_null_clears()
    {
        var id = await CreateValidAsync();
        await UpdateAsync(id, new { en = new { email = "a@b.co", site = "https://example.com" } });

        await UpdateAsync(id, new { en = new { email = (string?)null, site = "", pick = (string?)null } });

        var read = await _svc.GetAsync(Collection, id);
        var en = ((IReadOnlyDictionary<string, Dictionary<string, object?>>)read!["translations"]!)["en"];
        en["email"].Should().BeNull();
        en["site"].Should().Be("");
    }

    [Fact]
    public async Task Update_rejects_an_invalid_value_and_keeps_the_stored_one()
    {
        var id = await CreateValidAsync();
        await UpdateAsync(id, new { en = new { email = "a@b.co" } });

        var ex = await Rejected(UpdateAsync(id, new { en = new { email = "bad" } }));

        ex.Message.Should().Be("Field 'email' must be a valid email address for locale 'en'.");
        var read = await _svc.GetAsync(Collection, id);
        var en = ((IReadOnlyDictionary<string, Dictionary<string, object?>>)read!["translations"]!)["en"];
        en["email"].Should().Be("a@b.co");
    }

    // Order: Required (complete report) -> format -> MaxLength.

    [Fact]
    public async Task Missing_required_is_reported_before_a_format_error()
    {
        var ex = await Rejected(CreateAsync(new Dictionary<string, object>
        {
            ["en"] = new { title = "T" },
            ["zh-TW"] = new { email = "bad" },
        })!);

        ex.Message.Should().Be("Required translation field 'title' is missing for locale 'zh-TW'.");
    }

    [Fact]
    public async Task Format_error_is_reported_before_max_length()
    {
        var ex = await Rejected(CreateAsync(new
        {
            en = new { title = "T", email = "not-an-email-and-longer-than-twenty" },
        })!);

        ex.Message.Should().Be("Field 'email' must be a valid email address for locale 'en'.");
    }

    [Fact]
    public async Task Max_length_still_applies_to_a_well_formed_value()
    {
        var ex = await Rejected(CreateAsync(new
        {
            en = new { title = "T", email = "a@" + new string('b', 20) + ".co" },
        })!);

        ex.Message.Should().Be("Field 'email' exceeds maximum length 20 for locale 'en'.");
    }
}
