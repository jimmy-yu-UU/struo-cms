using AwesomeAssertions;
using Struo.Infrastructure.Migrations;
using Xunit;

namespace Struo.Tests.Migrations;

public sealed class CoreSeedDataTests
{
    [Fact]
    public void Redacted_replaces_only_the_admin_password_hash()
    {
        var withAdmin = new CoreSeedData([], "a@example.com", "$argon2id$real", ["article"]);

        var redacted = withAdmin.Redacted();

        redacted.AdminPasswordHash.Should().Be("<redacted>");
        redacted.AdminEmail.Should().Be("a@example.com");
        redacted.PublicReadCollections.Should().Equal("article");
        withAdmin.AdminPasswordHash.Should().Be("$argon2id$real");
        var noAdmin = new CoreSeedData([], null, null, []);
        noAdmin.Redacted().Should().Be(noAdmin);
    }
}
