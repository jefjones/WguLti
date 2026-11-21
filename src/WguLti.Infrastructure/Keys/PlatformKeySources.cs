using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using WguLti.Core;
using WguLti.Core.Security;

namespace WguLti.Infrastructure.Keys;

public sealed class FilePlatformKeySource : IPlatformKeySource
{
    private readonly string _path;
    private readonly object _gate = new();
    private SigningMaterial? _material;

    public FilePlatformKeySource(IOptions<LtiOptions> options)
    {
        var configured = options.Value.Signing.KeyPath;
        _path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wgulti", "dev-signing-key.pem")
            : configured;
    }

    public SigningMaterial GetActive()
    {
        if (_material is not null) return _material;
        lock (_gate)
        {
            if (_material is not null) return _material;
            var full = Path.GetFullPath(_path);
            var directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            RSA rsa;
            if (File.Exists(full))
            {
                rsa = RSA.Create();
                rsa.ImportFromPem(File.ReadAllText(full));
            }
            else
            {
                rsa = RSA.Create(2048);
                File.WriteAllText(full, rsa.ExportPkcs8PrivateKeyPem() + Environment.NewLine);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(full, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            _material = new SigningMaterial { Kid = RsaJwtSigner.KidFor(rsa), Rsa = rsa };
            return _material;
        }
    }

    public IReadOnlyList<PublishedKey> GetPublicKeys()
    {
        var active = GetActive();
        var (n, e) = RsaJwtSigner.PublicComponents(active.Rsa);
        return [new PublishedKey(active.Kid, n, e)];
    }
}

/// <summary>
/// Production signer. AWS KMS is intentionally not wired in this starter (Phase 6).
/// </summary>
public sealed class KmsPlatformKeySource : IPlatformKeySource
{
    public SigningMaterial GetActive() =>
        throw new NotSupportedException(
            "KMS signing is Phase 6 and is not configured. Set Lti:Signing:Provider to File for local development.");

    public IReadOnlyList<PublishedKey> GetPublicKeys() => GetActive() is null ? [] : [];
}
