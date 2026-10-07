using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

public class BootstrapAdminPasswordWarningTests
{
    [Fact]
    public void Warns_when_production_uses_default_password()
    {
        var logger = new ListLogger();
        BootstrapAdminPasswordWarning.LogIfDefault(isProduction: true, "admin", logger);
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning)
            .Which.Message.Should().Contain("default password 'admin'").And.Contain("Auth__BootstrapAdmin__Password");
    }

    [Fact]
    public void No_warning_when_production_password_is_non_default()
    {
        var logger = new ListLogger();
        BootstrapAdminPasswordWarning.LogIfDefault(isProduction: true, "s3cret-not-default", logger);
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public void No_warning_outside_production_even_with_default_password()
    {
        var logger = new ListLogger();
        BootstrapAdminPasswordWarning.LogIfDefault(isProduction: false, "admin", logger);
        logger.Entries.Should().BeEmpty();
    }
}
