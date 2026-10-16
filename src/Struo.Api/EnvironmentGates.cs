namespace Struo.Api;

/// <summary>
/// Resolves the settings that gate behavior by environment. A configured value wins; an unset
/// value falls back to the environment, the way <c>GraphQl:ExposeSchema</c> does.
/// </summary>
internal static class EnvironmentGates
{
    internal const string OpenApiEnabledKey = "OpenApi:Enabled";
    internal const string CookieRequireSecureKey = "Auth:Cookie:RequireSecure";

    /// <summary>Whether the OpenAPI document and the Scalar explorer are mapped; Development-only when unset.</summary>
    internal static bool ResolveOpenApiEnabled(IConfiguration config, IHostEnvironment env) =>
        config.GetValue<bool?>(OpenApiEnabledKey) ?? env.IsDevelopment();

    /// <summary>Whether the session cookie is always Secure; every environment except Development when unset.</summary>
    internal static bool ResolveCookieRequireSecure(IConfiguration config, IHostEnvironment env) =>
        config.GetValue<bool?>(CookieRequireSecureKey) ?? !env.IsDevelopment();
}
