using System.Net.Http.Json;
using System.Reflection;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Query;
using Struo.Infrastructure.Query;
using Struo.Tests.Support;
using Xunit;
using static Struo.Tests.Api.RelationWriteHarness;

namespace Struo.Tests.Api;

// One update reads the translation rows of its item at most once for the file-link checks, however many of
// them need the stored links. The count of LoadTranslationsAsync calls of an update that sends no file field is
// the baseline; an update that makes the checks compare translated file ids costs exactly one more.
[Collection("ApiIntegration")]
public sealed class LinkExistenceTranslationLoadTests : IClassFixture<NoPublicRelationsFixture>
{
    private const string Article = "fxArticle";
    private const string OgImage = "seoOgImageId";

    private readonly NoPublicRelationsApiFactory factory;
    private readonly RelationWriteHarness h;

    public LinkExistenceTranslationLoadTests(NoPublicRelationsFixture fixture)
    {
        factory = fixture.Factory;
        h = new(factory);
    }

    public class CountingRepository : DispatchProxy
    {
        public IItemRepository Target { get; set; } = null!;
        public int Loads;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(IItemRepository.LoadTranslationsAsync)) Interlocked.Increment(ref Loads);
            try
            {
                return targetMethod.Invoke(Target, args);
            }
            catch (TargetInvocationException e) when (e.InnerException is not null)
            {
                throw e.InnerException;
            }
        }
    }

    private async Task<int> LoadsOfUpdateAsync(string[] read, Dictionary<string, object?> fields, Dictionary<string, object?>? translation, Guid article)
    {
        CountingRepository? counter = null;
        using var host = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddScoped<IItemRepository>(sp =>
        {
            var proxy = DispatchProxy.Create<IItemRepository, CountingRepository>();
            counter = (CountingRepository)(object)proxy;
            counter.Target = ActivatorUtilities.CreateInstance<SqlSugarItemRepository>(sp);
            return proxy;
        })));
        var (email, password, _) = await factory.SeedEditorAsync(read, [Article]);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Struo-CSRF", "1");
        (await client.PostAsJsonAsync("/api/auth/login", new { email, password })).EnsureSuccessStatusCode();

        var outcome = await SendAsync(client, Rest, Article, article, fields, translation: translation);

        AssertWritten(outcome);
        return counter!.Loads;
    }

    [Fact]
    public async Task The_file_link_checks_share_one_load_of_the_translation_rows()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (stored, other) = (await h.SeedFileAsync(), await h.SeedFileAsync());
        var article = await SeedArticleAsync(admin);
        AssertWritten(await SendAsync(admin, Rest, Article, article, new(), translation: new() { [OgImage] = stored.ToString() }));

        var baseline = await LoadsOfUpdateAsync([Article, "file"], new() { ["status"] = "published" }, new() { ["title"] = "again" }, article);
        var unchangedWithoutRead = await LoadsOfUpdateAsync([Article], new(), new() { [OgImage] = stored.ToString() }, article);
        var changedWithRead = await LoadsOfUpdateAsync([Article, "file"], new(), new() { [OgImage] = other.ToString() }, article);

        unchangedWithoutRead.Should().Be(baseline + 1);
        changedWithRead.Should().Be(baseline + 1);
    }
}
