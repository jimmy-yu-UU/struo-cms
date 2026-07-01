using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class ExternalLoginProvisioningTests(ApiFactory factory)
{
    private static ExternalLoginPolicy Open => new(RequireEmailVerified: false, AllowedTenantId: null, AllowedEmailDomains: []);

    private async Task<ExternalLoginResult> ResolveAsync(ExternalIdentity id, ExternalLoginPolicy policy)
    {
        using var scope = factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IExternalLoginService>();
        return await svc.ResolveOrProvisionAsync(id, policy);
    }

    private async Task<T> QueryDbAsync<T>(Func<ISqlSugarClient, Task<T>> body)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        return await body(db);
    }

    [Fact]
    public async Task First_login_jit_provisions_active_roleless_user()
    {
        var email = $"jit-{Guid.NewGuid():N}@corp.com";
        var r = await ResolveAsync(new ExternalIdentity("iss", null, email, null, "JIT"), Open);

        r.Succeeded.Should().BeTrue();
        r.Provisioned.Should().BeTrue();

        var row = await QueryDbAsync(db => db.Queryable<User>().Where(u => u.Email == email).FirstAsync());
        row.Should().NotBeNull();
        row!.IsActive.Should().BeTrue();
        row.Password.Should().BeEmpty();
        var roleCount = await QueryDbAsync(db => db.Queryable<UserRole>().Where(ur => ur.UserId == row.Id).CountAsync());
        roleCount.Should().Be(0);
    }

    [Fact]
    public async Task Second_login_matches_existing_and_does_not_duplicate()
    {
        var email = $"dup-{Guid.NewGuid():N}@corp.com";
        var a = await ResolveAsync(new ExternalIdentity("iss", null, email, null, "A"), Open);
        var b = await ResolveAsync(new ExternalIdentity("iss", null, email, null, "A"), Open);

        a.UserId.Should().Be(b.UserId);
        b.Provisioned.Should().BeFalse();

        var count = await QueryDbAsync(db => db.Queryable<User>().Where(u => u.Email == email).CountAsync());
        count.Should().Be(1);
    }

    [Fact]
    public async Task Require_email_verified_rejects_and_provisions_nothing()
    {
        var email = $"unverified-{Guid.NewGuid():N}@corp.com";
        var strict = new ExternalLoginPolicy(RequireEmailVerified: true, AllowedTenantId: null, AllowedEmailDomains: []);

        var r = await ResolveAsync(new ExternalIdentity("iss", null, email, null, "U"), strict);

        r.Succeeded.Should().BeFalse();
        r.Failure.Should().Be(ExternalLoginFailure.EmailNotVerified);

        var count = await QueryDbAsync(db => db.Queryable<User>().Where(u => u.Email == email).CountAsync());
        count.Should().Be(0);
    }

    [Fact]
    public async Task Inactive_matched_user_is_rejected()
    {
        var email = $"inactive-{Guid.NewGuid():N}@corp.com";
        await QueryDbAsync(db => db.Insertable(new User
        {
            Id = Guid.CreateVersion7(), Email = email, Password = "", IsActive = false
        }).ExecuteCommandAsync());

        var r = await ResolveAsync(new ExternalIdentity("iss", null, email, null, "I"), Open);

        r.Succeeded.Should().BeFalse();
        r.Failure.Should().Be(ExternalLoginFailure.Inactive);
    }
}
