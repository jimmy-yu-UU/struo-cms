namespace Struo.Application.Configuration;

/// <summary>Config-bound password rules, bound from the <c>Auth:Password</c> section; absence of the
/// section keeps these defaults. Enforced by <see cref="Struo.Application.Security.PasswordPolicy"/>
/// at every write path that accepts a plaintext password.
/// <para>
/// <see cref="MaxLength"/> is a sanity bound, NOT a security control: Argon2id's cost is fixed by its
/// own time/memory/parallelism parameters, not by input length, so a long password does not amplify
/// hashing work. Only <see cref="MinLength"/> is published to the SPA (via <c>GET /api/config</c>)
/// because the client needs it for inline validation and for the generator's length; the maximum is
/// server-enforced only, deliberately.
/// </para></summary>
public sealed class PasswordPolicyOptions
{
    public const string SectionName = "Auth:Password";

    /// <summary>Minimum accepted plaintext length.</summary>
    public int MinLength { get; set; } = 8;

    /// <summary>Maximum accepted plaintext length (sanity bound — see the type remarks).</summary>
    public int MaxLength { get; set; } = 128;
}
