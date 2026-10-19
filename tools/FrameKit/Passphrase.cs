using System.Text;

namespace FrameKit;

public static class Passphrase
{
    public const string Variable = "FRAMEKIT_KEY_PASSPHRASE";
    public const int MinimumLength = 8;

    public static string ForExistingKey(string path)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(Variable);
        if (fromEnvironment is not null)
        {
            return fromEnvironment;
        }
        return Prompt($"Enter passphrase for {path}: ") ??
               throw new KitError($"please run this command in a terminal or set {Variable}");
    }

    public static string ForNewKey(string path)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(Variable);
        string passphrase;
        if (fromEnvironment is not null)
        {
            passphrase = fromEnvironment;
        }
        else
        {
            passphrase = Prompt($"New passphrase for {path}: ") ??
                         throw new KitError($"please create a protected key in a terminal or set {Variable}");
            if (Prompt("Type it again: ") != passphrase)
            {
                throw new KitError("passphrases do not match, please try again");
            }
        }
        if (passphrase.Length < MinimumLength)
        {
            throw new KitError($"a passphrase needs at least {MinimumLength} characters");
        }
        return passphrase;
    }

    private static string? Prompt(string prompt)
    {
        if (Console.IsInputRedirected)
        {
            return null;
        }
        Console.Error.Write(prompt);
        var text = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (text.Length > 0)
                {
                    text.Length--;
                }
                continue;
            }
            if (!char.IsControl(key.KeyChar))
            {
                text.Append(key.KeyChar);
            }
        }
        Console.Error.WriteLine();
        return text.ToString();
    }
}
