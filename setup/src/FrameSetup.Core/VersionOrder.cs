namespace FrameKit.Setup;

public static class VersionOrder
{
    public static int Compare(string a, string b)
    {
        var (coreA, preReleaseA) = Split(a);
        var (coreB, preReleaseB) = Split(b);
        var byCore = CompareParts(coreA, coreB);
        if (byCore != 0 || (preReleaseA is null && preReleaseB is null))
        {
            return byCore;
        }
        if (preReleaseA is null)
        {
            return 1;
        }
        if (preReleaseB is null)
        {
            return -1;
        }
        return CompareParts(preReleaseA, preReleaseB);
    }

    private static (string Core, string? PreRelease) Split(string version)
    {
        var dash = version.IndexOf('-');
        return dash < 0 ? (version, null) : (version[..dash], version[(dash + 1)..]);
    }

    private static int CompareParts(string a, string b)
    {
        var partsA = a.Length == 0 ? [] : a.Split('.');
        var partsB = b.Length == 0 ? [] : b.Split('.');
        for (var i = 0; i < partsA.Length && i < partsB.Length; i++)
        {
            var byPart = ComparePart(partsA[i], partsB[i]);
            if (byPart != 0)
            {
                return byPart;
            }
        }
        return partsA.Length.CompareTo(partsB.Length);
    }

    private static int ComparePart(string a, string b)
    {
        var numberA = a.Length > 0 && a.All(char.IsAsciiDigit);
        var numberB = b.Length > 0 && b.All(char.IsAsciiDigit);
        if (numberA && numberB)
        {
            var digitsA = a.TrimStart('0');
            var digitsB = b.TrimStart('0');
            return digitsA.Length != digitsB.Length
                ? digitsA.Length.CompareTo(digitsB.Length)
                : Math.Sign(string.CompareOrdinal(digitsA, digitsB));
        }
        if (numberA != numberB)
        {
            return numberA ? -1 : 1;
        }
        return Math.Sign(string.CompareOrdinal(a, b));
    }
}
