using System.Security.Cryptography.X509Certificates;
using Struo.Application.Configuration;

namespace Struo.Api.Auth;

/// <summary>
/// The certificates named by <c>DataProtection:Certificate</c>. A failure message names the file and the
/// configuration key, and never contains the password or the underlying exception text.
/// </summary>
internal sealed class DataProtectionCertificates : IDisposable
{
    private readonly List<X509Certificate2> _all;

    private DataProtectionCertificates(X509Certificate2 current, List<X509Certificate2> previous)
    {
        Current = current;
        Previous = previous;
        _all = [current, .. previous];
    }

    public X509Certificate2 Current { get; }

    public IReadOnlyList<X509Certificate2> Previous { get; }

    /// <summary>Every certificate that can decrypt a key: the current one, then the previous ones.</summary>
    public IReadOnlyList<X509Certificate2> Decrypting => _all;

    /// <summary>Loads the certificates, or returns null when no <c>Certificate:Path</c> is set.</summary>
    /// <exception cref="InvalidOperationException">A configured file cannot be loaded.</exception>
    public static DataProtectionCertificates? Load(StruoDataProtectionOptions options)
    {
        var certificate = options.Certificate;
        if (string.IsNullOrWhiteSpace(certificate.Path)) return null;

        var loaded = new List<X509Certificate2>();
        try
        {
            var current = LoadFile("DataProtection:Certificate:Path", certificate.Path, certificate.Password);
            loaded.Add(current);
            var previous = new List<X509Certificate2>();
            for (var i = 0; i < certificate.Previous.Count; i++)
            {
                var entry = certificate.Previous[i];
                var key = $"DataProtection:Certificate:Previous:{i}:Path";
                if (string.IsNullOrWhiteSpace(entry.Path))
                    throw new InvalidOperationException($"{key} must not be blank.");
                var certificateFile = LoadFile(key, entry.Path, entry.Password);
                loaded.Add(certificateFile);
                previous.Add(certificateFile);
            }

            return new DataProtectionCertificates(current, previous);
        }
        catch
        {
            foreach (var cert in loaded) cert.Dispose();
            throw;
        }
    }

    private static X509Certificate2 LoadFile(string key, string path, string? password)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"{key} '{path}' does not exist.");
        try
        {
            return X509CertificateLoader.LoadPkcs12FromFile(path, string.IsNullOrEmpty(password) ? null : password);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or IOException
                                       or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"{key} '{path}' could not be loaded as a PKCS#12 file ({ex.GetType().Name}); " +
                "check the file and DataProtection:Certificate:Password.");
        }
    }

    public void Dispose()
    {
        foreach (var cert in _all) cert.Dispose();
    }
}
