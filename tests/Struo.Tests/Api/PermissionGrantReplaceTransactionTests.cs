using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// <c>ReplaceAsync</c> joins an ambient transaction instead of committing its own, so a caller that
/// rolls back leaves the previous grants untouched.
/// </summary>
[Collection("ApiIntegration")]
public class PermissionGrantReplaceTransactionTests(ApiFactory factory)
{
    [Fact]
    public async Task ReplaceAsync_inside_a_rolled_back_outer_transaction_persists_nothing()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var store = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        var roleId = Guid.CreateVersion7();
        await db.Insertable(new Role { Id = roleId, Name = $"grant-tx-{roleId:N}" }).ExecuteCommandAsync();
        await store.ReplaceAsync(roleId, [new PermissionGrant("article", true, false, false)]);

        await db.Ado.BeginTranAsync();
        await store.ReplaceAsync(roleId, [new PermissionGrant("category", true, true, true)]);
        await db.Ado.RollbackTranAsync();

        var grants = await store.ListAsync(roleId);
        grants.Should().ContainSingle().Which.Collection.Should().Be("article");
    }
}
