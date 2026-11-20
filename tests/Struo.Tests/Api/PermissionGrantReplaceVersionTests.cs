using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Security;
using Struo.Domain.Query;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Struo.Tests.Api;

/// <summary>
/// The version guard of <c>ReplaceAsync</c> at the store level: one conditional update that runs first,
/// inside the same transaction as the grant rewrite.
/// </summary>
[Collection("ApiIntegration")]
public class PermissionGrantReplaceVersionTests(ApiFactory factory, ITestOutputHelper output)
{
    private static readonly PermissionGrant[] Article = [new("fxArticle", true, false, false)];

    private static async Task<Guid> InsertRoleAsync(ISqlSugarClient db)
    {
        var roleId = Guid.CreateVersion7();
        await db.Insertable(new Role { Id = roleId, Name = $"grant-ver-{roleId:N}", Version = 4 }).ExecuteCommandAsync();
        return roleId;
    }

    private static Task<long> VersionOfAsync(ISqlSugarClient db, Guid roleId) =>
        db.Queryable<Role>().Where(r => r.Id == roleId).Select(r => r.Version).FirstAsync();

    [Fact]
    public async Task The_version_check_is_the_first_statement_and_a_single_conditional_update()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var store = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        var roleId = await InsertRoleAsync(db);

        var statements = new List<string>();
        db.Aop.OnLogExecuting = (sql, _) => statements.Add(sql);
        var version = await store.ReplaceAsync(roleId, Article, 4);

        version.Should().Be(5);
        foreach (var s in statements) output.WriteLine(s);
        statements.Should().NotBeEmpty();
        statements[0].Should().StartWith("UPDATE", "the compare-and-advance is the first write");
        var where = statements[0][statements[0].IndexOf("WHERE", StringComparison.OrdinalIgnoreCase)..];
        where.Should().Contain("Version", "the update is conditional on the expected version");
        statements.Skip(1).Should().NotContain(s => s.StartsWith("UPDATE", StringComparison.Ordinal),
            "the version is advanced by exactly one statement");
    }

    [Fact]
    public async Task A_stale_expected_version_throws_and_changes_neither_grants_nor_version()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var store = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        var roleId = await InsertRoleAsync(db);
        await store.ReplaceAsync(roleId, Article, 4);

        var act = () => store.ReplaceAsync(roleId, [new PermissionGrant("fxTag", true, true, true)], 4);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        (await store.ListAsync(roleId)).Should().ContainSingle().Which.Collection.Should().Be("fxArticle");
        (await VersionOfAsync(db, roleId)).Should().Be(5);
    }

    [Fact]
    public async Task Without_an_expected_version_the_version_advances_by_one()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var store = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        var roleId = await InsertRoleAsync(db);

        (await store.ReplaceAsync(roleId, Article)).Should().Be(5);
        (await VersionOfAsync(db, roleId)).Should().Be(5);
    }

    [Fact]
    public async Task A_missing_role_returns_null_with_or_without_an_expected_version_and_writes_no_grants()
    {
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        var roleId = Guid.CreateVersion7();

        long? withoutVersion = await store.ReplaceAsync(roleId, Article);
        long? withVersion = await store.ReplaceAsync(roleId, Article, 4);

        withoutVersion.Should().BeNull();
        withVersion.Should().BeNull();
        (await store.ListAsync(roleId)).Should().BeEmpty();
    }

    [Fact]
    public async Task The_version_advance_rolls_back_with_an_outer_transaction()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var store = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        var roleId = await InsertRoleAsync(db);

        await db.Ado.BeginTranAsync();
        await store.ReplaceAsync(roleId, Article, 4);
        await db.Ado.RollbackTranAsync();

        (await VersionOfAsync(db, roleId)).Should().Be(4);
        (await store.ListAsync(roleId)).Should().BeEmpty();
    }
}
