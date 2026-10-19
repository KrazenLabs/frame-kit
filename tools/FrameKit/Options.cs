namespace FrameKit;

public sealed class Options
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _flags = new(StringComparer.Ordinal);

    private Options()
    {
    }

    public static Options Parse(IReadOnlyList<string> args, IReadOnlySet<string> valueNames, IReadOnlySet<string> flagNames)
    {
        var options = new Options();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (flagNames.Contains(arg))
            {
                options._flags.Add(arg);
            }
            else if (valueNames.Contains(arg))
            {
                if (i + 1 >= args.Count)
                {
                    throw new KitError($"{arg} needs a value");
                }
                options._values[arg] = args[++i];
            }
            else
            {
                throw new KitError($"unknown option '{arg}'");
            }
        }
        return options;
    }

    public bool Has(string flag) => _flags.Contains(flag);

    public string? Get(string name) => _values.TryGetValue(name, out var value) ? value : null;

    public string Require(string name) => Get(name) ?? throw new KitError($"{name} is required");
}
