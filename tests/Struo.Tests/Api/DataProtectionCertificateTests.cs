using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AwesomeAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Struo.Api.Auth;
using Struo.Application.Configuration;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// <c>DataProtection:Certificate</c> encrypts the key ring at rest (with or without Redis), previous
/// certificates only decrypt, and <c>RequireKeyProtection</c> makes a missing certificate a startup failure.
/// </summary>
public sealed class DataProtectionCertificateTests : IDisposable
{
    private const string Password = "pfx-pass-9d41";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "struo-dp-" + Guid.NewGuid().ToString("N"));

    public DataProtectionCertificateTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string NewPfx(string name, string password = Password)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var path = Path.Combine(_dir, name + ".pfx");
        File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, password));
        return path;
    }

    private string KeyDir => Path.Combine(_dir, "keys");

    private ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(config)
            .AddStruoDataProtection();
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(KeyDir));
        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> Certificate(string path, params string[] previous)
    {
        var settings = new Dictionary<string, string?>
        {
            ["DataProtection:Certificate:Path"] = path,
            ["DataProtection:Certificate:Password"] = Password,
        };
        for (var i = 0; i < previous.Length; i++)
        {
            settings[$"DataProtection:Certificate:Previous:{i}:Path"] = previous[i];
            settings[$"DataProtection:Certificate:Previous:{i}:Password"] = Password;
        }
        return settings;
    }

    private static byte[] Protect(ServiceProvider sp) =>
        sp.GetRequiredService<IDataProtectionProvider>().CreateProtector("t").Protect([1, 2, 3, 4]);

    private static byte[] Unprotect(ServiceProvider sp, byte[] payload) =>
        sp.GetRequiredService<IDataProtectionProvider>().CreateProtector("t").Unprotect(payload);

    private string KeyRingXml() =>
        string.Concat(Directory.GetFiles(KeyDir, "*.xml").Select(File.ReadAllText));

    [Fact]
    public void Without_a_certificate_the_key_is_stored_in_plain_text()
    {
        using var sp = Build([]);
        Protect(sp);

        KeyRingXml().Should().Contain("<masterKey");
    }

    [Fact]
    public void A_certificate_encrypts_the_stored_key_and_the_key_still_round_trips()
    {
        var pfx = NewPfx("current");
        byte[] payload;
        using (var sp = Build(Certificate(pfx))) payload = Protect(sp);

        KeyRingXml().Should().Contain("encryptedSecret").And.NotContain("<masterKey");
        using var restarted = Build(Certificate(pfx));
        Unprotect(restarted, payload).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void A_previous_certificate_decrypts_keys_written_under_it()
    {
        var oldPfx = NewPfx("old");
        var newPfx = NewPfx("new");
        byte[] payload;
        using (var sp = Build(Certificate(oldPfx))) payload = Protect(sp);

        using var rotated = Build(Certificate(newPfx, oldPfx));

        Unprotect(rotated, payload).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void Without_the_previous_certificate_old_keys_cannot_be_read()
    {
        var oldPfx = NewPfx("old");
        var newPfx = NewPfx("new");
        byte[] payload;
        using (var sp = Build(Certificate(oldPfx))) payload = Protect(sp);

        using var rotated = Build(Certificate(newPfx));

        var act = () => Unprotect(rotated, payload);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void A_previous_certificate_does_not_encrypt_new_keys()
    {
        var oldPfx = NewPfx("old");
        var newPfx = NewPfx("new");
        byte[] payload;
        using (var sp = Build(Certificate(newPfx, oldPfx))) payload = Protect(sp);

        // Only the current certificate is needed to read what it wrote.
        using var onlyCurrent = Build(Certificate(newPfx));
        Unprotect(onlyCurrent, payload).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void A_ring_encrypted_with_a_certificate_cannot_be_read_when_restarted_without_one()
    {
        var pfx = NewPfx("current");
        byte[] payload;
        using (var sp = Build(Certificate(pfx))) payload = Protect(sp);

        using var withoutCertificate = Build([]);

        var act = () => Unprotect(withoutCertificate, payload);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void The_persisted_decryptor_type_is_stable_across_builds()
    {
        using (var sp = Build(Certificate(NewPfx("current")))) Protect(sp);

        var xml = KeyRingXml();

        xml.Should().Contain("decryptorType=\"Struo.Api.Auth.CertificateKeyDecryptor, Struo.Api,");
        xml.Should().Contain("Version=1.0.0.0");

        // Only AssemblyVersion is pinned: the file version keeps following the product version.
        var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(CertificateKeyDecryptor).Assembly.Location);
        info.FileVersion.Should().StartWith(info.ProductVersion!.Split('+')[0]);
    }

    [Fact]
    public void A_blank_previous_certificate_path_fails_validation()
    {
        var settings = Certificate(NewPfx("current"));
        settings["DataProtection:Certificate:Previous:0:Path"] = "";
        settings["DataProtection:Certificate:Previous:0:Password"] = Password;

        Validate(settings).Failures.Should().ContainSingle(f => f.Contains("DataProtection:Certificate:Previous:0:Path"));
    }

    private static OptionsValidationException Validate(Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(config)
            .AddStruoDataProtection()
            .BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptions<StruoDataProtectionOptions>>().Value;
        return act.Should().Throw<OptionsValidationException>().Which;
    }

    [Fact]
    public void RequireKeyProtection_without_a_certificate_fails_validation() =>
        Validate(new() { ["DataProtection:RequireKeyProtection"] = "true" })
            .Failures.Should().ContainSingle(f => f.Contains("DataProtection:RequireKeyProtection")
                                                  && f.Contains("DataProtection:Certificate:Path"));

    [Fact]
    public void A_missing_certificate_file_fails_validation_naming_the_path_and_never_the_password()
    {
        var missing = Path.Combine(_dir, "nope.pfx");

        var failure = Validate(Certificate(missing)).Failures.Should().ContainSingle().Which;

        failure.Should().Contain(missing).And.NotContain(Password);
    }

    [Fact]
    public void A_wrong_certificate_password_fails_validation_without_leaking_it()
    {
        var pfx = NewPfx("current");
        var settings = Certificate(pfx);
        settings["DataProtection:Certificate:Password"] = "wrong-pass-77";

        var failure = Validate(settings).Failures.Should().ContainSingle().Which;

        failure.Should().Contain(pfx).And.NotContain("wrong-pass-77").And.NotContain(Password);
    }

    [Fact]
    public void A_missing_previous_certificate_fails_validation()
    {
        var pfx = NewPfx("current");
        var missing = Path.Combine(_dir, "gone.pfx");

        Validate(Certificate(pfx, missing)).Failures.Should().ContainSingle(f => f.Contains(missing));
    }

    [Fact]
    public void RequireKeyProtection_with_a_valid_certificate_passes()
    {
        var settings = Certificate(NewPfx("current"));
        settings["DataProtection:RequireKeyProtection"] = "true";
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(config)
            .AddStruoDataProtection()
            .BuildServiceProvider();

        provider.GetRequiredService<IOptions<StruoDataProtectionOptions>>().Value.RequireKeyProtection
            .Should().BeTrue();
    }
}

/// <summary>
/// The same validation fails host startup, in every environment. A generic host is driven directly
/// because <c>Program</c> logs and swallows a startup exception.
/// </summary>
public sealed class DataProtectionRequireKeyProtectionStartupTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task RequireKeyProtection_without_a_certificate_stops_the_host(string environment)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment });
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["DataProtection:RequireKeyProtection"] = "true" });
        builder.Services.AddStruoDataProtection();
        using var host = builder.Build();

        var act = () => host.StartAsync();

        (await act.Should().ThrowAsync<OptionsValidationException>())
            .Which.Message.Should().Contain("DataProtection:RequireKeyProtection");
    }
}
