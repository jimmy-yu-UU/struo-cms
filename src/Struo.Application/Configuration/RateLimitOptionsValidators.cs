using Microsoft.Extensions.Options;

namespace Struo.Application.Configuration;

/// <summary>
/// Allowed ranges of the numeric <c>RateLimiting:*</c> keys. The lower bounds are what the framework's
/// fixed-window and concurrency limiters accept (they reject a non-positive permit limit or window and a
/// negative queue); the upper bounds keep a mistyped value from meaning "never limits" or "never expires":
/// a window of a day at most, a million attempts per window at most, and, because every concurrent login
/// holds a 64 MiB Argon2id working set, at most 1000 concurrent logins. A key is validated whether or not
/// its section is <c>Enabled</c>, and there is no value that means "no limit": switch a limiter off with
/// <c>Enabled=false</c>.
/// </summary>
public static class RateLimitBounds
{
    public const int MinPermitLimit = 1;
    public const int MaxPermitLimit = 1_000_000;
    public const int MinWindowSeconds = 1;
    public const int MaxWindowSeconds = 86_400;
    public const int MinConcurrentLogins = 1;
    public const int MaxConcurrentLogins = 1_000;
    public const int MinQueueLimit = 0;
    public const int MaxQueueLimit = 10_000;

    internal static void Check(List<string> failures, string section, string key, int value, int min, int max)
    {
        if (value < min || value > max)
            failures.Add($"{section}:{key} ({value}) must be between {min} and {max}.");
    }

    internal static ValidateOptionsResult Result(List<string> failures) =>
        failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);

    internal static void CheckWindow(
        List<string> failures, string section, int permitLimit, int windowSeconds)
    {
        Check(failures, section, "PermitLimit", permitLimit, MinPermitLimit, MaxPermitLimit);
        Check(failures, section, "WindowSeconds", windowSeconds, MinWindowSeconds, MaxWindowSeconds);
    }
}

/// <summary>Range rules for <see cref="LoginRateLimitOptions"/>; see <see cref="RateLimitBounds"/>.</summary>
public sealed class LoginRateLimitOptionsValidator : IValidateOptions<LoginRateLimitOptions>
{
    public ValidateOptionsResult Validate(string? name, LoginRateLimitOptions options)
    {
        var failures = new List<string>();
        RateLimitBounds.CheckWindow(failures, LoginRateLimitOptions.SectionName, options.PermitLimit, options.WindowSeconds);
        return RateLimitBounds.Result(failures);
    }
}

/// <summary>Range rules for <see cref="LoginAccountRateLimitOptions"/>; see <see cref="RateLimitBounds"/>.</summary>
public sealed class LoginAccountRateLimitOptionsValidator : IValidateOptions<LoginAccountRateLimitOptions>
{
    public ValidateOptionsResult Validate(string? name, LoginAccountRateLimitOptions options)
    {
        var failures = new List<string>();
        RateLimitBounds.CheckWindow(
            failures, LoginAccountRateLimitOptions.SectionName, options.PermitLimit, options.WindowSeconds);
        return RateLimitBounds.Result(failures);
    }
}

/// <summary>Range rules for <see cref="PasswordRateLimitOptions"/>; see <see cref="RateLimitBounds"/>.</summary>
public sealed class PasswordRateLimitOptionsValidator : IValidateOptions<PasswordRateLimitOptions>
{
    public ValidateOptionsResult Validate(string? name, PasswordRateLimitOptions options)
    {
        var failures = new List<string>();
        RateLimitBounds.CheckWindow(failures, PasswordRateLimitOptions.SectionName, options.PermitLimit, options.WindowSeconds);
        return RateLimitBounds.Result(failures);
    }
}

/// <summary>Range rules for <see cref="LoginConcurrencyOptions"/>; see <see cref="RateLimitBounds"/>.</summary>
public sealed class LoginConcurrencyOptionsValidator : IValidateOptions<LoginConcurrencyOptions>
{
    public ValidateOptionsResult Validate(string? name, LoginConcurrencyOptions options)
    {
        var failures = new List<string>();
        RateLimitBounds.Check(
            failures, LoginConcurrencyOptions.SectionName, "PermitLimit", options.PermitLimit,
            RateLimitBounds.MinConcurrentLogins, RateLimitBounds.MaxConcurrentLogins);
        RateLimitBounds.Check(
            failures, LoginConcurrencyOptions.SectionName, "QueueLimit", options.QueueLimit,
            RateLimitBounds.MinQueueLimit, RateLimitBounds.MaxQueueLimit);
        return RateLimitBounds.Result(failures);
    }
}
