using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Struo.Api.Auth;
using Struo.Application.Files;
using Struo.Application.Security;
using Struo.Infrastructure.DependencyInjection;
using Xunit;

namespace Struo.Tests.DependencyInjection;

/// <summary>
/// ConfigurationBinder APPENDS bound array elements to whatever the property already holds, so a
/// non-empty C# default survives alongside the configured value and narrowing silently fails. These
/// two properties are the only non-empty collection defaults in src/ (AllowedContentTypes and
/// AllowedEmailDomains already default to [], and for AllowedContentTypes empty means ALLOW ALL, so
/// neither may be given a non-empty default to "fix" it).
/// </summary>
public sealed class CollectionOptionDefaultsTests
{
    private static TOptions Bind<TOptions>(
        Dictionary<string, string?> settings,
        Action<IServiceCollection, IConfiguration> register) where TOptions : class
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        register(services, configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<TOptions>>().Value;
    }

    [Fact]
    public void Narrowing_Oidc_scopes_replaces_the_default_instead_of_appending_to_it()
    {
        var bound = Bind<OidcOptions>(
            new Dictionary<string, string?> { ["Oidc:Scopes:0"] = "openid" },
            (services, config) => services.AddStruoOidc(config));

        bound.Scopes.Should().Equal("openid");
    }

    [Fact]
    public void Omitting_Oidc_scopes_still_yields_the_documented_default()
    {
        var bound = Bind<OidcOptions>(
            new Dictionary<string, string?> { ["Oidc:Enabled"] = "false" },
            (services, config) => services.AddStruoOidc(config));

        bound.Scopes.Should().Equal(OidcOptions.DefaultScopes);
    }

    [Fact]
    public void Narrowing_allowed_image_formats_replaces_the_default_instead_of_appending_to_it()
    {
        var bound = Bind<FileStorageOptions>(
            new Dictionary<string, string?>
            {
                ["Struo:Files:Backend"] = "local",
                ["Struo:Files:Local:RootPath"] = "App_Data/uploads",
                ["Struo:Files:ImageTransform:AllowedFormats:0"] = "webp"
            },
            (services, _) => services.AddStruoFiles());

        bound.ImageTransform.AllowedFormats.Should().Equal("webp");
    }

    [Fact]
    public void Omitting_allowed_image_formats_still_yields_the_documented_default()
    {
        var bound = Bind<FileStorageOptions>(
            new Dictionary<string, string?>
            {
                ["Struo:Files:Backend"] = "local",
                ["Struo:Files:Local:RootPath"] = "App_Data/uploads"
            },
            (services, _) => services.AddStruoFiles());

        bound.ImageTransform.AllowedFormats.Should()
            .Equal(FileStorageOptions.ImageTransformOptions.DefaultAllowedFormats);
    }
}
