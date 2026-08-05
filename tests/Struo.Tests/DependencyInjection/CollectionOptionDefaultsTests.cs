using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
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

    /// <summary>
    /// Binds through <c>AddStruoOidc</c> with OIDC enabled and resolves the actual
    /// <see cref="OpenIdConnectOptions"/> the handler runs with — the object <c>opts.Scopes</c> (the
    /// SEPARATE local copy <c>AddStruoOidc</c> reads via <c>config.GetSection(...).Get&lt;OidcOptions&gt;()</c>,
    /// not the container-resolved <see cref="OidcOptions"/>) feeds via <c>options.Scope</c>. This is the
    /// bind site <see cref="Omitting_Oidc_scopes_still_yields_the_documented_default"/> and
    /// <see cref="Narrowing_Oidc_scopes_replaces_the_default_instead_of_appending_to_it"/> above cannot
    /// see, because both of those resolve <see cref="IOptions{TOptions}"/> of <see cref="OidcOptions"/>
    /// — the container copy, normalized by the separate <c>PostConfigure</c> registration. <c>AddDataProtection</c>
    /// is required here because resolving <see cref="OpenIdConnectOptions"/> triggers ASP.NET Core's own
    /// <c>OpenIdConnectPostConfigureOptions</c>, which needs an <c>IDataProtectionProvider</c>.
    /// </summary>
    private static OpenIdConnectOptions BindOidcHandlerOptions(Dictionary<string, string?> overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Oidc:Enabled"] = "true",
            ["Oidc:Authority"] = "https://login.example.com/tenant/v2.0",
            ["Oidc:ClientId"] = "test-client",
            ["Oidc:ClientSecret"] = "test-secret"
        };
        foreach (var (key, value) in overrides) settings[key] = value;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDataProtection();
        services.AddStruoOidc(configuration);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(AuthSchemes.Oidc);
    }

    [Fact]
    public void Omitting_Oidc_scopes_still_configures_the_OpenIdConnect_handler_with_the_documented_default()
    {
        var handlerOptions = BindOidcHandlerOptions(new Dictionary<string, string?>());

        handlerOptions.Scope.Should().Equal(OidcOptions.DefaultScopes);
    }

    [Fact]
    public void Narrowing_Oidc_scopes_configures_the_OpenIdConnect_handler_with_only_the_narrowed_scope()
    {
        var handlerOptions = BindOidcHandlerOptions(
            new Dictionary<string, string?> { ["Oidc:Scopes:0"] = "openid" });

        handlerOptions.Scope.Should().Equal("openid");
    }
}
