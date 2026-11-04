using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Application.Security;
using Struo.Infrastructure.Migrations.Commands;

namespace Struo.Infrastructure.Identity;

/// <summary>
/// <c>auth:unlock --email &lt;email&gt;</c>: the break-glass way to clear a per-account login lock when no
/// administrator can sign in. The count lives in Redis only when <c>Redis:ConnectionString</c> is set;
/// without it the count is in the API process's memory, out of reach of a separate command process, so the
/// command says that a restart clears it and fails rather than claiming success. The email is never echoed.
/// </summary>
internal static class AuthUnlockCommand
{
    public const string Name = "auth:unlock";
    private const string Usage = $"Usage: {Name} --email <email>";

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args, IServiceProvider services, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        if (!TryParse(args, out var email))
        {
            await stderr.WriteLineAsync(Usage);
            return MigrationCli.Usage;
        }

        if (string.IsNullOrWhiteSpace(services.GetRequiredService<IConfiguration>().GetValue<string>("Redis:ConnectionString")))
        {
            await stderr.WriteLineAsync(
                $"{Name}: no Redis is configured, so the login attempt count lives in the memory of the API process, " +
                "which this command cannot reach. Restart the API to clear every login lock.");
            return MigrationCli.Failure;
        }

        if (!services.GetRequiredService<IOptions<LoginAccountRateLimitOptions>>().Value.Enabled)
        {
            await stdout.WriteLineAsync($"{Name}: the per-account login lock is disabled, so there is no lock to clear.");
            return MigrationCli.Success;
        }

        try
        {
            await services.GetRequiredService<ILoginAttemptThrottle>().ResetAsync(email, ct);
        }
        catch (Exception ex)
        {
            // The exception text can carry the connection string, so only its type is printed.
            await stderr.WriteLineAsync(
                $"{Name}: could not reach Redis ({ex.GetType().Name}). Check Redis:ConnectionString and that the server is running.");
            return MigrationCli.Failure;
        }

        await stdout.WriteLineAsync($"{Name}: cleared the login lock for the given account, if it had one.");
        return MigrationCli.Success;
    }

    // Accepts exactly one `--email <value>` or `--email=<value>`; anything else is a usage error.
    private static bool TryParse(IReadOnlyList<string> args, out string email)
    {
        email = "";
        string? value = null;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (value is not null || !IsEmailFlag(arg)) return false;
            if (arg.StartsWith("--email=", StringComparison.Ordinal)) value = arg["--email=".Length..];
            else if (i + 1 < args.Count) value = args[++i];
            else return false;
        }

        if (!EmailAddress.IsUsable(value)) return false;
        email = value!;
        return true;
    }

    private static bool IsEmailFlag(string arg) =>
        arg == "--email" || arg.StartsWith("--email=", StringComparison.Ordinal);
}
