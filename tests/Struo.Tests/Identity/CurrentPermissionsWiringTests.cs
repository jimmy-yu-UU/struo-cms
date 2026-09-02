using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Security;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

[Collection("ApiIntegration")]
public class CurrentPermissionsWiringTests(ApiFactory factory)
{
    [Fact]
    public void Reader_and_writer_resolve_to_the_same_scoped_instance()
    {
        using var scope = factory.Services.CreateScope();

        var reader = scope.ServiceProvider.GetRequiredService<ICurrentPermissions>();
        var writer = scope.ServiceProvider.GetRequiredService<ICurrentPermissionsWriter>();

        writer.Should().BeSameAs(reader);
    }

    [Fact]
    public void Reader_interface_has_no_setter()
    {
        typeof(ICurrentPermissions).GetMethod("Set").Should().BeNull();
        typeof(ICurrentPermissionsWriter).GetMethod("Set").Should().NotBeNull();
    }

    [Fact]
    public void A_write_through_the_writer_is_visible_through_the_reader()
    {
        using var scope = factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<ICurrentPermissionsWriter>();
        var reader = scope.ServiceProvider.GetRequiredService<ICurrentPermissions>();

        writer.Set(new EffectivePermissions(true,
            new Dictionary<string, (bool, bool, bool)>(StringComparer.OrdinalIgnoreCase)));

        reader.Current.IsSuperAdmin.Should().BeTrue();
    }
}
