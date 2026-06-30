using Isopoh.Cryptography.Argon2;
using Struo.Application.Security;

namespace Struo.Infrastructure.Identity;

/// <summary>Argon2id hasher producing a self-contained PHC-encoded string
/// (salt + parameters embedded). No separate salt column is needed.</summary>
public sealed class Argon2idPasswordHasher : IPasswordHasher
{
    public string Hash(string password) =>
        Argon2.Hash(
            password,
            timeCost: 3,
            memoryCost: 65536,
            parallelism: 1,
            type: Argon2Type.HybridAddressing,
            hashLength: 32,
            secureArrayCall: null);

    public bool Verify(string encoded, string password) =>
        Argon2.Verify(encoded, password, secureArrayCall: null);
}
