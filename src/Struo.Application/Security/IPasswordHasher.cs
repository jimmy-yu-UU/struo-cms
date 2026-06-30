namespace Struo.Application.Security;

/// <summary>Hashes and verifies passwords. The encoded string is self-contained
/// (algorithm, parameters, and salt embedded) — no separate salt storage.</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string encoded, string password);
}
