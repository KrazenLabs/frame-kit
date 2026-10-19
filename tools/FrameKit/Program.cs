using FrameKit;

const string Usage = """
    framekit: package Steam Frame apps with FrameKit

      framekit key [--path <file>] [--passphrase]
          Creates a new signing key at ~/.framekit/release-key.pem
          Create an encrypted key using a passphrase (recommended).

      framekit key --protect [--path <file>]
          Encrypts an existing key with a passphrase.

      framekit release --manifest <frameapp.json> --payload <folder> --out <folder> [options]
          Builds the full setup release files.

      framekit package --manifest <frameapp.json> --payload <folder> --out <folder> [options]
          Builds only the signed package.

      framekit setup --manifest <frameapp.json> --package <file.tar.gz> --out <folder> [--platforms <list>]
          Builds only the setup files.

    Options:
      --key <file>          signing key (default: ~/.framekit/release-key.pem)
      --next-key <file>     define a new key to migrate to with this update
      --platforms <list>    default: win-x64,linux-x64
      --allow-file-urls     for local testing only: accept file:/// update URLs
    """;

string[] packageValues = ["--manifest", "--payload", "--out", "--key", "--panel", "--epoch", "--panel-key", "--next-key"];
string[] packageFlags = ["--no-panel", "--allow-file-urls"];

try
{
    if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
    {
        Console.WriteLine(Usage);
        return args.Length == 0 ? 2 : 0;
    }
    var rest = args[1..];
    switch (args[0])
    {
        case "key":
        {
            var options = Options.Parse(rest, new HashSet<string> { "--path" }, new HashSet<string> { "--passphrase", "--protect" });
            var path = Path.GetFullPath(options.Get("--path") ?? KitPaths.DefaultKey);
            if (options.Has("--protect"))
            {
                if (options.Has("--passphrase"))
                {
                    throw new KitError("use either --protect or --passphrase, not both");
                }
                var protectedKey = ReleaseKey.Protect(path);
                Console.WriteLine($"Signing key {path} ({protectedKey}) is now encrypted.");
                Console.WriteLine("Please make sure to replace any old copies you have.");
                return 0;
            }
            var created = ReleaseKey.Create(path, options.Has("--passphrase"));
            Console.WriteLine($"Signing key created: {path} ({created})");
            Console.WriteLine("Keep it backed up and secure! Never share it or put it in a repository!");
            return 0;
        }
        case "package":
        {
            var options = Options.Parse(rest, packageValues.ToHashSet(), packageFlags.ToHashSet());
            Packager.Build(PackageSettings(options));
            return 0;
        }
        case "setup":
        {
            var options = Options.Parse(rest, new HashSet<string> { "--manifest", "--package", "--out", "--platforms" }, new HashSet<string>());
            SetupBuilder.Build(new SetupBuilder.Settings(options.Require("--manifest"), options.Require("--package"),
                                                         options.Require("--out"), Platforms(options)));
            return 0;
        }
        case "common":
        {
            var options = Options.Parse(rest, new HashSet<string> { "--to", "--check" }, new HashSet<string> { "--seal" });
            var modes = new[] { options.Has("--seal"), options.Get("--to") is not null, options.Get("--check") is not null };
            if (modes.Count(mode => mode) != 1)
            {
                throw new KitError("common needs exactly one of --to <folder>, --check <folder> or --seal");
            }
            if (options.Has("--seal"))
            {
                Console.WriteLine($"Sealed FrameKit common {CommonModule.Seal(KitPaths.Common)} in {KitPaths.Common}");
            }
            else if (options.Get("--to") is { } to)
            {
                var target = Path.GetFullPath(to);
                Console.WriteLine($"Copied FrameKit common {CommonModule.Copy(KitPaths.Common, target)} to {target}");
            }
            else
            {
                var target = Path.GetFullPath(options.Require("--check"));
                Console.WriteLine($"{target}: FrameKit common {CommonModule.Check(KitPaths.Common, target)}");
            }
            return 0;
        }
        case "panel-sign":
        {
            var options = Options.Parse(rest, new HashSet<string> { "--key", "--panel", "--public" }, new HashSet<string>());
            var panel = Path.GetFullPath(options.Get("--panel") ?? KitPaths.PanelBuild);
            var version = PanelSignature.Sign(panel, options.Get("--key") ?? KitPaths.DefaultPanelKey,
                                              options.Get("--public") ?? KitPaths.PanelPublicKey);
            Console.WriteLine($"Signed shared panel {version} in {panel}");
            return 0;
        }
        case "release":
        {
            var options = Options.Parse(rest, packageValues.Append("--platforms").ToHashSet(), packageFlags.ToHashSet());
            var package = Packager.Build(PackageSettings(options));
            SetupBuilder.Build(new SetupBuilder.Settings(options.Require("--manifest"), package, options.Require("--out"), Platforms(options)));
            Console.WriteLine($"Release built in {Path.GetFullPath(options.Require("--out"))}.");
            return 0;
        }
        default:
            Console.Error.WriteLine($"unknown command '{args[0]}'\n");
            Console.Error.WriteLine(Usage);
            return 2;
    }
}
catch (KitError error)
{
    Console.Error.WriteLine($"error: {error.Message}");
    return 1;
}

static Packager.Settings PackageSettings(Options options)
{
    var epochText = options.Get("--epoch") ?? Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
    long epoch;
    if (epochText is null)
    {
        epoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
    else if (!long.TryParse(epochText, out epoch) || epoch < 0)
    {
        throw new KitError($"'{epochText}' is not a unix time");
    }
    return new Packager.Settings(
        options.Require("--manifest"),
        options.Require("--payload"),
        options.Require("--out"),
        options.Get("--key") ?? KitPaths.DefaultKey,
        options.Has("--no-panel") ? null : options.Get("--panel") ?? KitPaths.PanelBuild,
        epoch,
        options.Has("--allow-file-urls"),
        options.Get("--panel-key"),
        options.Get("--next-key"));
}

static IReadOnlyList<string> Platforms(Options options) =>
    options.Get("--platforms")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? SetupBuilder.DefaultPlatforms;
