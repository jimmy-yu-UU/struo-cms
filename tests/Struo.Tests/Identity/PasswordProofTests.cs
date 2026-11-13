using System.Reflection;
using AwesomeAssertions;
using Struo.Application.Security;
using Xunit;

namespace Struo.Tests.Identity;

public class PasswordProofTests
{
    private const string Hash = "enc:secret-hash";

    [Fact]
    public void A_proof_created_from_a_stored_hash_matches_that_hash_only()
    {
        var proof = PasswordProof.From(Hash);

        proof.Matches(Hash).Should().BeTrue();
        proof.Matches("enc:other-hash").Should().BeFalse();
        proof.Matches(Hash.ToUpperInvariant()).Should().BeFalse("the comparison is ordinal");
        proof.Matches("").Should().BeFalse();
    }

    [Fact]
    public void A_proof_prints_only_its_type_name()
    {
        PasswordProof.From(Hash).ToString().Should().Be("PasswordProof").And.NotContain(Hash);
    }

    [Fact]
    public void Nothing_public_exposes_the_hash()
    {
        var members = typeof(PasswordProof).GetMembers(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        // The only string-typed public surface is the factory's parameter and Matches' parameter;
        // no property, field or return value carries the hash.
        members.OfType<PropertyInfo>().Should().BeEmpty();
        members.OfType<FieldInfo>().Should().BeEmpty();
        members.OfType<MethodInfo>()
            .Where(m => m.Name != nameof(object.ToString))
            .Should().OnlyContain(m => m.ReturnType != typeof(string) && m.ReturnType != typeof(char[]) && m.ReturnType != typeof(byte[]));
    }

    [Fact]
    public async Task A_proof_built_through_the_public_surface_drives_the_built_in_confirmation()
    {
        var id = Guid.CreateVersion7();
        var store = new MutableStore(new UserCredential(id, Hash, true, "a@b.com"));
        var service = new AuthService(store, new PlainHasher());

        (await service.ConfirmUnchangedAsync(AuthResult.Ok(id, PasswordProof.From(Hash)))).Should().BeTrue();
        (await service.ConfirmUnchangedAsync(AuthResult.Ok(id, PasswordProof.From("enc:other")))).Should().BeFalse();
    }

    private sealed class MutableStore(UserCredential? current) : IUserCredentialStore
    {
        public Task<UserCredential?> FindByEmailAsync(string email, CancellationToken ct = default) => Task.FromResult(current);
        public Task<UserCredential?> FindByAccessTokenAsync(string tokenHash, CancellationToken ct = default) => Task.FromResult<UserCredential?>(null);
        public Task<UserCredential?> FindByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(current);
        public Task TouchAccessTokenLastUsedAsync(Guid userId, DateTime nowUtc, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class PlainHasher : IPasswordHasher
    {
        public string Hash(string p) => "enc:" + p;
        public bool Verify(string encoded, string p) => encoded == "enc:" + p;
    }
}
