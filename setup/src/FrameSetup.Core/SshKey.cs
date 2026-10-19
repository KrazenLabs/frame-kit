using System.Security.Cryptography;
using System.Text;

namespace FrameKit.Setup;

public sealed class SshKey
{
    public const string Comment = FrameKitConstants.PairingName;
    public const string KeyOverrideVariable = "FRAMEKIT_SETUP_KEY";
    public const string PrivateKeyFile = "id_rsa";
    public const string ProtectedKeyFile = "id_rsa.dpapi";
    private const int KeyBits = 3072;
    private const string PublicKeyFile = "id_rsa.pub";
    private const string SetAsideSuffix = ".unreadable";
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private SshKey(string privateKeyText, string publicKeyLine)
    {
        PrivateKeyText = privateKeyText;
        PublicKeyLine = publicKeyLine;
        var blob = Convert.FromBase64String(publicKeyLine.Split(' ')[1]);
        Fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(blob)).TrimEnd('=');
    }

    public string PrivateKeyText { get; }
    public string PublicKeyLine { get; }
    public string Fingerprint { get; }

    public static bool IsOverridden => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(KeyOverrideVariable));

    public static SshKey LoadOrCreate(string directory) => LoadOrCreate(directory, mayRetry: true);

    public static void SetAside(string directory)
    {
        foreach (var name in new[] { ProtectedKeyFile, PrivateKeyFile, PublicKeyFile })
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
            {
                File.Move(path, path + SetAsideSuffix, overwrite: true);
            }
        }
    }

    private static SshKey LoadOrCreate(string directory, bool mayRetry)
    {
        var overridden = Environment.GetEnvironmentVariable(KeyOverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return FromFile(overridden);
        }
        CreateDirectory(directory);
        var plainPath = Path.Combine(directory, PrivateKeyFile);
        var protectedPath = Path.Combine(directory, ProtectedKeyFile);
        if (OperatingSystem.IsWindows())
        {
            if (File.Exists(protectedPath))
            {
                try
                {
                    return FromPem(Encoding.ASCII.GetString(KeyProtection.Unprotect(File.ReadAllBytes(protectedPath))));
                }
                catch (CryptographicException)
                {
                    File.Move(protectedPath, protectedPath + SetAsideSuffix, overwrite: true);
                }
            }
            else if (File.Exists(plainPath))
            {
                var existing = FromPem(File.ReadAllText(plainPath));
                WriteNewKeyFile(protectedPath, KeyProtection.Protect(Encoding.ASCII.GetBytes(existing.PrivateKeyText)));
                File.Delete(plainPath);
                return existing;
            }
        }
        else if (File.Exists(plainPath))
        {
            if ((File.GetUnixFileMode(plainPath) & ~OwnerOnly) != 0)
            {
                File.SetUnixFileMode(plainPath, OwnerOnly);
            }
            return FromPem(File.ReadAllText(plainPath));
        }
        using var rsa = RSA.Create(KeyBits);
        var pem = new string(PemEncoding.Write("RSA PRIVATE KEY", rsa.ExportRSAPrivateKey()));
        var key = new SshKey(pem + "\n", OpenSshPublicKey(rsa.ExportParameters(false), Comment));
        var created = OperatingSystem.IsWindows()
            ? WriteNewKeyFile(protectedPath, KeyProtection.Protect(Encoding.ASCII.GetBytes(key.PrivateKeyText)))
            : WriteNewKeyFile(plainPath, Encoding.ASCII.GetBytes(key.PrivateKeyText));
        if (!created)
        {
            return mayRetry
                ? LoadOrCreate(directory, mayRetry: false)
                : throw new IOException($"Another Setup is creating this PC's pairing key in {directory} at the same time.");
        }
        ReplaceFile(Path.Combine(directory, PublicKeyFile), Encoding.ASCII.GetBytes(key.PublicKeyLine + "\n"));
        return key;
    }

    public static SshKey FromFile(string path)
    {
        var text = File.ReadAllText(path);
        if (!text.Contains("BEGIN OPENSSH PRIVATE KEY", StringComparison.Ordinal))
        {
            return FromPem(text);
        }
        var publicPath = path + ".pub";
        if (!File.Exists(publicPath))
        {
            throw new InvalidOperationException($"{path} is in OpenSSH format; Setup needs its public half next to it ({publicPath}).");
        }
        var parts = File.ReadAllText(publicPath).Trim().Split(' ');
        if (parts.Length < 2 || parts[0] != "ssh-rsa")
        {
            throw new InvalidOperationException($"{publicPath} isn't an ssh-rsa public key (the headset only accepts RSA).");
        }
        return new SshKey(text, parts[0] + " " + parts[1] + " " + Comment);
    }

    public static SshKey FromPem(string pem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        var pkcs1 = new string(PemEncoding.Write("RSA PRIVATE KEY", rsa.ExportRSAPrivateKey()));
        return new SshKey(pkcs1 + "\n", OpenSshPublicKey(rsa.ExportParameters(false), Comment));
    }

    public static string OpenSshPublicKey(RSAParameters parameters, string comment)
    {
        if (parameters.Exponent is null || parameters.Modulus is null)
        {
            throw new ArgumentException("The RSA key has no public part.", nameof(parameters));
        }
        using var blob = new MemoryStream();
        WriteString(blob, Encoding.ASCII.GetBytes("ssh-rsa"));
        WriteMpint(blob, parameters.Exponent);
        WriteMpint(blob, parameters.Modulus);
        return "ssh-rsa " + Convert.ToBase64String(blob.ToArray()) + " " + comment;
    }

    private static void WriteString(Stream stream, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        stream.Write(data);
    }

    private static void WriteMpint(Stream stream, byte[] bigEndianUnsigned)
    {
        var start = 0;
        while (start < bigEndianUnsigned.Length - 1 && bigEndianUnsigned[start] == 0)
        {
            start++;
        }
        var trimmed = bigEndianUnsigned[start..];
        if (trimmed.Length > 0 && (trimmed[0] & 0x80) != 0)
        {
            trimmed = [0, .. trimmed];
        }
        WriteString(stream, trimmed);
    }

    private static void CreateDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, OwnerOnly | UnixFileMode.UserExecute);
        }
    }

    private static bool WriteNewKeyFile(string path, byte[] content)
    {
        var temporary = TemporaryName(path);
        try
        {
            WriteDurably(temporary, content);
            if (OperatingSystem.IsWindows())
            {
                KeyProtection.RestrictToCurrentUser(temporary);
            }
            try
            {
                File.Move(temporary, path, overwrite: false);
                return true;
            }
            catch (IOException) when (File.Exists(path))
            {
                return false;
            }
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static void ReplaceFile(string path, byte[] content)
    {
        var temporary = TemporaryName(path);
        try
        {
            WriteDurably(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static string TemporaryName(string path) => $"{path}.{Guid.NewGuid():N}.tmp";

    private static void WriteDurably(string path, byte[] content)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerOnly;
        }
        using var stream = new FileStream(path, options);
        stream.Write(content);
        stream.Flush(flushToDisk: true);
    }
}
