using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace Struo.Api.Auth;

/// <summary>
/// The decryptor recorded in every key stored under <c>DataProtection:Certificate</c>: its full type name
/// and assembly name are persisted in the key ring. Never rename or move this type, and keep
/// <c>AssemblyVersion</c> pinned in <c>Struo.Api.csproj</c>, or keys already written cannot be read. See
/// <c>docs/ai/decisions/dataprotection-certificate-decryptor-type.md</c>.
/// </summary>
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
