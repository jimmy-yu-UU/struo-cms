// tests/Struo.Tests/Query/LiveRepositoryTests.CrossBackend.cs
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Infrastructure.Query;
using Struo.Sample.Blog;
using Xunit;

namespace Struo.Tests.Query;

public sealed partial class LiveRepositoryTests
{
    // Pairs whose COUNT(...) column reaches the reader under different provider types (SQL Server int,
    // the others bigint), in both orders.
    public static TheoryData<string, string> BackendPairs => new()
    {
        { "MySql", "SqlServer" }, { "SqlServer", "MySql" },
        { "MariaDb", "SqlServer" }, { "SqlServer", "MariaDb" },
        { "PostgreSQL", "SqlServer" }, { "SqlServer", "PostgreSQL" },
    };

    [Theory, MemberData(nameof(BackendPairs))]
    public async Task Own_field_facet_counts_bind_on_both_backends_of_one_process(string first, string second)
    {
        if (!LiveBackendConfigured(first) || !LiveBackendConfigured(second)) return;

        // SqlSugar keeps one row binder per result type and column signature for the whole process;
        // dropping the FacetRow<string> binders makes the first backend below the one that builds it.
        ReflectionInoCore<IDataReaderEntityBuilder<FacetRow<string>>>.GetInstance().RemoveAllCache();

        (await StatusFacet(first)).Should().Equal(new FacetBucket("published", 2), new FacetBucket("draft", 1));
        (await StatusFacet(second)).Should().Equal(new FacetBucket("published", 2), new FacetBucket("draft", 1));
    }

    private static bool LiveBackendConfigured(string backend) =>
        !string.IsNullOrWhiteSpace(Support.LiveBackend.Get(backend).Connection);

    private async Task<IReadOnlyList<FacetBucket>> StatusFacet(string backend)
    {
        _db?.Dispose();
        Use(backend);
        var (repo, graph, md) = BuildFacetRepo();
        await repo.CreateAsync("article", new Article { Id = Guid.NewGuid(), Status = "published" });
        await repo.CreateAsync("article", new Article { Id = Guid.NewGuid(), Status = "published" });
        await repo.CreateAsync("article", new Article { Id = Guid.NewGuid(), Status = "draft" });

        var facet = FacetPathResolver.Resolve(md.GetCollection("article")!, "status", graph, md);
        return await repo.FacetAsync(new FacetRequest(
            "article", new QueryModel(null, null, [], 100, 0, null), facet, [], null, DeletedFilter.Exclude, 50));
    }
}
