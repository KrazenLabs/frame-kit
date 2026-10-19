using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace FrameKit;

public sealed class ReleaseKey : IDisposable
{
    private const string P256Oid = "1.2.840.10045.3.1.7";
    private const string EncryptedPemLabel = "-----BEGIN ENCRYPTED PRIVATE KEY-----";
    private const string PublicPemLabel = "-----BEGIN PUBLIC KEY-----";
    private static readonly string[] P256Names = ["nistP256", "ECDSA_P256", "secp256r1", "prime256v1"];
    private static readonly PbeParameters Encryption = new(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000);
    private readonly ECDsa _key;

    private ReleaseKey(ECDsa key)
    {
        _key = key;
    }

    public string PublicKeyPem => _key.ExportSubjectPublicKeyInfoPem() + "\n";

    public string Fingerprint => FingerprintOf(_key);

    public static bool IsProtected(string path) => File.ReadAllText(path).Contains(EncryptedPemLabel, StringComparison.Ordinal);

    public static ReleaseKey Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new KitError("please create a signing key first");
        }
        var text = File.ReadAllText(path);
        var key = ECDsa.Create();
        try
        {
            if (text.Contains(EncryptedPemLabel, StringComparison.Ordinal))
            {
                var passphrase = Passphrase.ForExistingKey(path);
                try
                {
                    key.ImportFromEncryptedPem(text, passphrase);
                }
                catch (CryptographicException)
                {
                    throw new KitError("incorrect passphrase");
                }
            }
            else
            {
                key.ImportFromPem(text);
            }
            key.ExportParameters(true);
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            key.Dispose();
            throw new KitError($"{path} is not a valid key ({exception.Message})");
        }
        catch
        {
            key.Dispose();
            throw;
        }
        RequireP256(key);
        return new ReleaseKey(key);
    }

    public static (string Pem, string Fingerprint) LoadPublic(string path)
    {
        if (!File.Exists(path))
        {
            throw new KitError($"key not found: {path}");
        }
        var text = File.ReadAllText(path);
        if (!text.Contains(PublicPemLabel, StringComparison.Ordinal))
        {
            using var privateKey = Load(path);
            return (privateKey.PublicKeyPem, privateKey.Fingerprint);
        }
        using var key = ECDsa.Create();
        try
        {
            key.ImportFromPem(text);
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            throw new KitError($"{path} is not a valid key ({exception.Message})");
        }
        RequireP256(key);
        return (key.ExportSubjectPublicKeyInfoPem() + "\n", FingerprintOf(key));
    }

    public static string Create(string path, bool withPassphrase = false)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            throw new KitError($"a key already exists at {fullPath}");
        }
        var passphrase = withPassphrase ? Passphrase.ForNewKey(fullPath) : null;
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        WritePrivate(fullPath, Pem(key, passphrase), FileMode.CreateNew);
        return FingerprintOf(key);
    }

    public static string Protect(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new KitError($"signing key not found: {fullPath}");
        }
        if (IsProtected(fullPath))
        {
            throw new KitError($"{fullPath} is already encrypted");
        }
        using var key = Load(fullPath);
        var passphrase = Passphrase.ForNewKey(fullPath);
        var temporary = fullPath + ".new";
        File.Delete(temporary);
        WritePrivate(temporary, Pem(key._key, passphrase), FileMode.CreateNew);
        using (var check = ECDsa.Create())
        {
            check.ImportFromEncryptedPem(File.ReadAllText(temporary), passphrase);
            if (FingerprintOf(check) != key.Fingerprint)
            {
                File.Delete(temporary);
                throw new KitError("key does not match original key");
            }
        }
        File.Move(temporary, fullPath, overwrite: true);
        return key.Fingerprint;
    }

    public byte[] Sign(byte[] data) => _key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

    public static bool Verify(string publicKeyPem, byte[] data, byte[] signature)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(publicKeyPem);
        return key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    }

    public void Dispose() => _key.Dispose();

    private static string FingerprintOf(ECDsa key) =>
        "SHA256:" + Convert.ToHexStringLower(SHA256.HashData(key.ExportSubjectPublicKeyInfo()))[..16];

    private static void RequireP256(ECDsa key)
    {
        var curve = key.ExportParameters(false).Curve;
        var isP256 = key.KeySize == 256 &&
                     (curve.Oid.Value == P256Oid || P256Names.Contains(curve.Oid.FriendlyName, StringComparer.OrdinalIgnoreCase));
        if (!isP256)
        {
            key.Dispose();
            throw new KitError("signing keys must be ECDSA P-256");
        }
    }

    private static string Pem(ECDsa key, string? passphrase) =>
        (passphrase is null ? key.ExportECPrivateKeyPem() : key.ExportEncryptedPkcs8PrivateKeyPem(passphrase, Encryption)) + "\n";

    private static void WritePrivate(string fullPath, string pem, FileMode mode)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var options = new FileStreamOptions { Mode = mode, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }
        using (var stream = new FileStream(fullPath, options))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            writer.Write(pem);
        }
        if (OperatingSystem.IsWindows())
        {
            RestrictToCurrentUser(fullPath);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void RestrictToCurrentUser(string path)
    {
        var user = WindowsIdentity.GetCurrent().User ?? throw new KitError("cannot find user directory");
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }
}
