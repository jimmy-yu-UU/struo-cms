using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;

namespace Struo.Api.Auth;

/// <summary>
/// Encrypts the key ring with <c>DataProtection:Certificate:Path</c> and decrypts it with that certificate
/// or any of <c>Certificate:Previous</c>. The framework's certificate decryption list cannot be filled
/// from late-bound configuration, so the decryptor type recorded in each stored key is
/// <see cref="CertificateKeyDecryptor"/>, which delegates to the framework's own decryption.
/// </summary>
internal sealed class DataProtectionKeyProtection : IDisposable
{
    private readonly DataProtectionCertificates? _certificates;
    private readonly ServiceProvider? _decryptionServices;

    public DataProtectionKeyProtection(IOptions<StruoDataProtectionOptions> options, ILoggerFactory loggerFactory)
    {
        _certificates = DataProtectionCertificates.Load(options.Value);
        if (_certificates is null) return;

        Encryptor = new CertificateKeyEncryptor(
            new CertificateXmlEncryptor(_certificates.Current, loggerFactory));
        var services = new ServiceCollection();
        services.AddDataProtection().UnprotectKeysWithAnyCertificate([.. _certificates.Decrypting]);
        _decryptionServices = services.BuildServiceProvider();
        Decryptor = new EncryptedXmlDecryptor(_decryptionServices);
    }

    /// <summary>Null when no certificate is configured.</summary>
    public IXmlEncryptor? Encryptor { get; }

    public IXmlDecryptor? Decryptor { get; }

    public void Dispose()
    {
        _decryptionServices?.Dispose();
        _certificates?.Dispose();
    }

    private sealed class CertificateKeyEncryptor(CertificateXmlEncryptor inner) : IXmlEncryptor
    {
        public EncryptedXmlInfo Encrypt(System.Xml.Linq.XElement plaintextElement) =>
            new(inner.Encrypt(plaintextElement).EncryptedElement, typeof(CertificateKeyDecryptor));
    }
}

/// <summary>Decryptor recorded in keys stored under <see cref="DataProtectionKeyProtection"/>.</summary>
public sealed class CertificateKeyDecryptor(IServiceProvider services) : IXmlDecryptor
{
    public System.Xml.Linq.XElement Decrypt(System.Xml.Linq.XElement encryptedElement)
    {
        var decryptor = services.GetRequiredService<DataProtectionKeyProtection>().Decryptor
            ?? throw new InvalidOperationException(
                "A stored key is encrypted with a certificate but DataProtection:Certificate:Path is not set.");
        return decryptor.Decrypt(encryptedElement);
    }
}
