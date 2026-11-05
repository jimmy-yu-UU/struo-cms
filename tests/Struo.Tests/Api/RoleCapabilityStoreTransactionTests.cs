using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary><c>ReplaceAsync</c> on the capability store joins an ambient transaction, so a caller that rolls back
/// leaves the previous set untouched; a repeated code is a unique-index violation and rolls the replace back.</summary>
[Collection("ApiIntegration")]
public class RoleCapabilityStoreTransactionTests(ApiFactory factory)
{
    private static async Task<(ISqlSugarClient Db, IRoleCapabilityStore Store, Guid RoleId)> ArrangeAsync(IServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var store = scope.ServiceProvider.GetRequiredService<IRoleCapabilityStore>();
        var roleId = Guid.CreateVersion7();
        await db.Insertable(new Role { Id = roleId, Name = $"cap-tx-{roleId:N}" }).ExecuteCommandAsync();
        await store.ReplaceAsync(roleId, ["login.unlock"]);
        return (db, store, roleId);
    }

    [Fact]
    public async Task Replace_inside_a_rolled_back_outer_transaction_persists_nothing()
    {
        using var scope = factory.Services.CreateScope();
        var (db, store, roleId) = await ArrangeAsync(scope);

        await db.Ado.BeginTranAsync();
        await store.ReplaceAsync(roleId, ["other.code"]);
        await db.Ado.RollbackTranAsync();

        (await store.ListAsync(roleId)).Should().Equal("login.unlock");
    }

    [Fact]
    public async Task A_repeated_code_violates_the_unique_index_and_keeps_the_previous_set()
    {
        using var scope = factory.Services.CreateScope();
        var (_, store, roleId) = await ArrangeAsync(scope);

        var act = () => store.ReplaceAsync(roleId, ["a.b", "a.b"]);

        await act.Should().ThrowAsync<Exception>();
        (await store.ListAsync(roleId)).Should().Equal("login.unlock");
    }
}
