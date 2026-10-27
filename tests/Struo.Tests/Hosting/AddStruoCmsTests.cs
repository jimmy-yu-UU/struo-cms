// tests/Struo.Tests/Hosting/AddStruoCmsTests.cs
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Struo.Api.Auth;
using Struo.Api.Hosting;
using Struo.Application.Search;
using Struo.Application.Security;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Hosting;

/// <summary>
/// A fork's own <c>Program.cs</c> registers its services after <c>AddStruoCms()</c>; these tests build a
/// host exactly that way and check that the fork's registration wins over the core default.
/// </summary>
public class AddStruoCmsTests
{
    [Fact]
    public void A_service_registered_after_AddStruoCms_replaces_the_core_default()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddStruoCms();
        var custom = new ScriptedSearchProvider(_ => SearchOutcome.NotHandled);
        builder.Services.AddScoped<ISearchProvider>(_ => custom);

        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ISearchProvider>().Should().BeSameAs(custom);
    }

    [Fact]
    public void A_service_registered_before_AddStruoCms_is_kept_over_the_core_default()
    {
        var builder = WebApplication.CreateBuilder();
        var custom = new ScriptedSearchProvider(_ => SearchOutcome.NotHandled);
        builder.Services.AddScoped<ISearchProvider>(_ => custom);
        builder.AddStruoCms();

        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ISearchProvider>().Should().BeSameAs(custom);
    }

    [Fact]
    public void AddStruoCms_hands_the_configure_callback_a_StruoCmsBuilder()
    {
        var builder = WebApplication.CreateBuilder();
        StruoCmsBuilder? seen = null;

        builder.AddStruoCms(cms => seen = cms);

        seen.Should().NotBeNull();
    }

    [Theory]
    [InlineData(typeof(IHtmlSanitizer))]
    [InlineData(typeof(IPasswordHasher))]
    [InlineData(typeof(IFileAccessPolicy))]
    public void A_replaceable_core_default_registered_before_AddStruoCms_is_kept(Type serviceType)
    {
        var builder = WebApplication.CreateBuilder();
        Func<IServiceProvider, object> forkFactory = _ => throw new InvalidOperationException("not resolved by this test");
        builder.Services.AddSingleton(serviceType, forkFactory);

        builder.AddStruoCms();

        // The last descriptor is the one the container resolves: it must still be the fork's.
        builder.Services.Last(d => d.ServiceType == serviceType)
            .ImplementationFactory.Should().BeSameAs(forkFactory);
    }
}
