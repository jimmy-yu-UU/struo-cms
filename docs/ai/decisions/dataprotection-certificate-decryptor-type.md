# The certificate-protected key ring records its own decryptor type

## Decision

A key encrypted under `DataProtection:Certificate` is stored with `decryptorType` set to
`Struo.Api.Auth.CertificateKeyDecryptor, Struo.Api, Version=1.0.0.0, ...`
(`src/Struo.Api/Auth/CertificateKeyDecryptor.cs`). The encryptor wraps the framework's
`CertificateXmlEncryptor` and changes only that attribute; the payload is the framework's standard
`EncryptedData`. The decryptor delegates to the framework's `EncryptedXmlDecryptor`, built with the
current and the `Previous` certificates.

The type name and the assembly name are persisted in every encrypted key. Renaming or moving the class,
or changing the assembly name, makes existing keys unreadable. The assembly version is persisted too, and
a build that does not carry the same version cannot load a key written by another, so `Struo.Api.csproj`
pins `AssemblyVersion` to `1.0.0.0` (`Directory.Build.props` `Version` still drives the file and
informational versions).

To stop depending on the type, rewrite each key's `decryptorType` attribute to
`Microsoft.AspNetCore.DataProtection.XmlEncryption.EncryptedXmlDecryptor, Microsoft.AspNetCore.DataProtection`
in the key ring. The payload needs no change.

## Why

The framework decrypts a certificate-encrypted key through `XmlKeyDecryptionOptions`, which is internal,
and the certificates are only known from configuration read after service registration. Nothing public
can fill that list late. Rejected:

- Loading the certificates at registration and calling `UnprotectKeysWithAnyCertificate`: it splits
  validation (late, on the options) from wiring (early), and test hosts that append configuration after
  registration would validate one certificate and use another.
- Reflection on the internal options type: it breaks silently on a framework update.
- Replacing `IActivator`: it changes how every decryptor in the process is created.

## Evidence

Measured 2026-11-03: `The_persisted_decryptor_type_is_stable_across_builds` in
`tests/Struo.Tests/Api/DataProtectionCertificateTests.cs` asserts the persisted attribute and that the
file version still follows `Version`. A key ring
written with the type, restarted against the same Redis and certificate, was read back without writing a
new key.

## Unknowns

The framework's decryption also looks a certificate up by thumbprint in the operating system's
CurrentUser and LocalMachine `My` stores, so a certificate installed there may decrypt even when it is
not configured. That lookup was read from the framework's behaviour, not exercised here.

## Referenced from

- `src/Struo.Api/Auth/CertificateKeyDecryptor.cs`
- `src/Struo.Api/Struo.Api.csproj`
