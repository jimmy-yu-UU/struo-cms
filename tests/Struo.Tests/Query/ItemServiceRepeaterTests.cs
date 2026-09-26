using System.Text.Json;
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Revisions;
using Struo.Infrastructure.Security;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

public class ItemServiceRepeaterTests : IDisposable
{
    public sealed class Faq
    {
        [CmsField(Label = "Question", Interface = FieldInterface.Text, Required = true)]
        public string Question { get; set; } = "";

        [CmsField(Label = "Answer", Interface = FieldInterface.Textarea)]
        public string Answer { get; set; } = "";

        [CmsField(Label = "Category", Interface = FieldInterface.Select, MaxLength = 10)]
        [CmsOptions("general:General", "billing:Billing")]
        public string? Category { get; set; }
    }

    [SugarTable("repeater_thing")]
    [CmsCollection("RepeaterThing")]
    public sealed class RepeaterThing : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [CmsField(Label = "FAQs", Interface = FieldInterface.Repeater)]
        public List<Faq> Faqs { get; set; } = new();

        [CmsField(Label = "Required FAQs", Interface = FieldInterface.Repeater, Required = true)]
        public List<Faq> RequiredFaqs { get; set; } = new();
    }

    private readonly SqliteTestDatabase _file = new();
    private readonly ItemService _svc;

    public ItemServiceRepeaterTests()
    {
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<RepeaterThing>();
        db.CodeFirst.InitTables<Language>();
        db.CodeFirst.InitTables<Revision>();
        LanguageSeeder.SeedAsync(db, TestLocalization.Default).GetAwaiter().GetResult();

        var types = new[] { typeof(RepeaterThing) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["repeaterthing"] = typeof(RepeaterThing),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph, new StruoQueryOptions());
        var languages = new LanguageProvider(db);
        var revisionUser = new TestCurrentUserAccessor(Guid.Empty);
        var revisionStore = new SqlSugarRevisionStore(db, revisionUser);
        var snapshotBuilder = new RevisionSnapshotBuilder(repo, provider, registry, graph);
        _svc = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, languages, new StruoQueryOptions(), new GanssHtmlSanitizer(),
            revisionUser, revisionStore, snapshotBuilder, new NoopUserSessionRevocationService());
    }

    public void Dispose() => _file.Dispose();

    private static JsonElement Body(object obj)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(obj));
        return doc.RootElement.Clone();
    }

    private static object OneRequired => new { question = "r", answer = "", category = (string?)null };

    [Fact]
    public async Task Faqs_round_trip_in_order()
    {
        var body = Body(new
        {
            faqs = new[]
            {
                new { question = "問題一", answer = "答案", category = (string?)"general" },
                new { question = "Q2", answer = "", category = (string?)null },
            },
            requiredFaqs = new[] { OneRequired },
        });
        var created = await _svc.CreateAsync("repeaterthing", body);
        var read = await _svc.GetAsync("repeaterthing", created["id"]!.ToString()!);

        var faqs = ((IEnumerable<ItemServiceRepeaterTests.Faq>)read!["faqs"]!).ToList();
        faqs.Select(f => f.Question).Should().Equal("問題一", "Q2");
        faqs[0].Category.Should().Be("general");
    }

    [Fact]
    public async Task Fully_blank_rows_are_dropped()
    {
        var body = Body(new
        {
            faqs = new[]
            {
                new { question = "keep", answer = "", category = (string?)null },
                new { question = "  ", answer = "", category = (string?)null }, // all-blank -> dropped
            },
            requiredFaqs = new[] { OneRequired },
        });
        var created = await _svc.CreateAsync("repeaterthing", body);
        var read = await _svc.GetAsync("repeaterthing", created["id"]!.ToString()!);

        ((IEnumerable<ItemServiceRepeaterTests.Faq>)read!["faqs"]!).Should().ContainSingle();
    }

    [Fact]
    public async Task Missing_required_sub_field_is_rejected()
    {
        var body = Body(new
        {
            faqs = new[] { new { question = "", answer = "has answer", category = (string?)null } },
            requiredFaqs = new[] { OneRequired },
        });
        var act = () => _svc.CreateAsync("repeaterthing", body);
        await act.Should().ThrowAsync<QueryException>().WithMessage("*'question' is required*");
    }

    [Fact]
    public async Task Out_of_options_sub_field_value_is_rejected()
    {
        var body = Body(new
        {
            faqs = new[] { new { question = "q", answer = "", category = "mars" } },
            requiredFaqs = new[] { OneRequired },
        });
        var act = () => _svc.CreateAsync("repeaterthing", body);
        await act.Should().ThrowAsync<QueryException>().WithMessage("*not in its options*");
    }

    [Fact]
    public async Task Over_length_sub_field_value_is_rejected()
    {
        var body = Body(new
        {
            faqs = new[] { new { question = "q", answer = "", category = new string('x', 11) } },
            requiredFaqs = new[] { OneRequired },
        });
        var act = () => _svc.CreateAsync("repeaterthing", body);
        await act.Should().ThrowAsync<QueryException>().WithMessage("*exceeds maximum length 10*");
    }

    [Fact]
    public async Task Required_repeater_empty_is_rejected()
    {
        var body = Body(new { faqs = new object[0] }); // requiredFaqs omitted
        var act = () => _svc.CreateAsync("repeaterthing", body);
        await act.Should().ThrowAsync<QueryException>().WithMessage("Field 'requiredFaqs' is required.");
    }

    [Fact]
    public async Task Non_object_element_is_rejected_as_bad_request()
    {
        var body = Body(new { faqs = new[] { "not-an-object" }, requiredFaqs = new[] { OneRequired } });
        var act = () => _svc.CreateAsync("repeaterthing", body);
        await act.Should().ThrowAsync<QueryException>(); // STJ JsonException -> QueryException (400)
    }

    [Fact]
    public async Task Update_omitting_the_required_faqs_keeps_the_stored_value()
    {
        var created = await _svc.CreateAsync("repeaterthing", Body(new { requiredFaqs = new[] { OneRequired } }));
        var id = created["id"]!.ToString()!;

        var updated = await _svc.UpdateAsync("repeaterthing", id,
            Body(new { faqs = new[] { new { question = "new", answer = "", category = (string?)null } } }));

        updated.Should().NotBeNull();
        var requiredFaqs = ((IEnumerable<Faq>)updated!["requiredFaqs"]!).ToList();
        requiredFaqs.Select(f => f.Question).Should().Equal("r");
        var faqs = ((IEnumerable<Faq>)updated["faqs"]!).ToList();
        faqs.Select(f => f.Question).Should().Equal("new");
    }

    [Fact]
    public async Task Update_sending_an_empty_array_for_the_required_faqs_is_rejected()
    {
        var created = await _svc.CreateAsync("repeaterthing", Body(new { requiredFaqs = new[] { OneRequired } }));
        var id = created["id"]!.ToString()!;

        var act = () => _svc.UpdateAsync("repeaterthing", id, Body(new { requiredFaqs = Array.Empty<object>() }));

        await act.Should().ThrowAsync<QueryException>().WithMessage("Field 'requiredFaqs' is required.");
    }
}
