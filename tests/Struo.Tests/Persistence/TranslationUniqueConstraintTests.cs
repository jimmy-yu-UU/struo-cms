using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Fixtures;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

/// <summary>
/// A translation sidecar must not hold two rows for the same (foreign key, locale) — that would
/// make overlay reads non-deterministic. SqlSugarClientFactory's EntityService hook derives the
/// composite UNIQUE on the FK + Locale columns from a <see cref="TranslationSidecarIndexPolicy"/>
/// built from the fixture's <c>[CmsTranslations]</c> metadata, which CodeFirst <c>InitTables</c>
/// materialises as a unique index (the same mechanism the revisions backstop uses). CodeFirst
/// creates the matching physical index on any backend where these tables do not already exist.
/// </summary>
public sealed class TranslationUniqueConstraintTests
{
    private static readonly TranslationSidecarIndexPolicy Policy = TranslationSidecarIndexPolicy.FromMetadata(
        MetadataScanner.ScanTypes([
            typeof(FxArticle), typeof(FxCategory), typeof(FxTag),
            typeof(Struo.Infrastructure.Files.File), typeof(MediaFolder),
        ]));

    private static (ITestDatabase, ISqlSugarClient) NewClient()
    {
        var db = TestBackend.CreateUnitDatabase();
        var client = SqlSugarClientFactory.Create(
            db.Options(),
            new TestCurrentUserAccessor(Guid.Empty), Policy);
        return (db, client);
    }

    [Fact]
    public void Article_translation_rejects_duplicate_fk_locale()
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables<FxArticleTranslation>();
            var articleId = Guid.NewGuid();

            client.Insertable(new FxArticleTranslation { FxArticleId = articleId, Locale = "en", Title = "First" })
                .ExecuteCommand();

            var act = () => client.Insertable(
                new FxArticleTranslation { FxArticleId = articleId, Locale = "en", Title = "Dup" }).ExecuteCommand();

            act.Should().Throw<Exception>();
        }
    }

    [Fact]
    public void File_translation_rejects_duplicate_fk_locale()
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables<FileTranslation>();
            var fileId = Guid.NewGuid();

            client.Insertable(new FileTranslation { FileId = fileId, Locale = "en", Title = "First" })
                .ExecuteCommand();

            var act = () => client.Insertable(
                new FileTranslation { FileId = fileId, Locale = "en", Title = "Dup" }).ExecuteCommand();

            act.Should().Throw<Exception>();
        }
    }

    [Fact]
    public void Article_translation_allows_same_fk_different_locale()
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables<FxArticleTranslation>();
            var articleId = Guid.NewGuid();

            client.Insertable(new FxArticleTranslation { FxArticleId = articleId, Locale = "en", Title = "EN" })
                .ExecuteCommand();

            var act = () => client.Insertable(
                new FxArticleTranslation { FxArticleId = articleId, Locale = "fr", Title = "FR" }).ExecuteCommand();

            act.Should().NotThrow();
        }
    }
}
