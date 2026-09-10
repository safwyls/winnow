using System.Numerics;
using System.Text.RegularExpressions;

namespace Winnow.App.Services;

internal sealed partial record ReleaseVersion(string Text, Version Number, string[] Pre) : IComparable<ReleaseVersion>
{
    [GeneratedRegex(@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$")]
    private static partial Regex Pattern();

    public static ReleaseVersion? Parse(string value)
    {
        var match = Pattern().Match(value);
        if (!match.Success || !Version.TryParse(string.Join('.', match.Groups[1].Value,
                match.Groups[2].Value, match.Groups[3].Value), out var number)) return null;
        var pre = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];
        if (pre.Any(p => p.All(char.IsAsciiDigit) && p.Length > 1 && p[0] == '0')) return null;
        return new(value, number, pre);
    }

    public bool IsDevelopment => Pre.Length > 0 && Pre[0] is "dev" or "ci";

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        var result = Number.CompareTo(other.Number);
        if (result != 0) return result;
        if (Pre.Length == 0 || other.Pre.Length == 0)
            return Pre.Length == other.Pre.Length ? 0 : Pre.Length == 0 ? 1 : -1;
        for (var i = 0; i < Math.Min(Pre.Length, other.Pre.Length); i++)
        {
            var leftNumeric = BigInteger.TryParse(Pre[i], out var left) && Pre[i].All(char.IsAsciiDigit);
            var rightNumeric = BigInteger.TryParse(other.Pre[i], out var right) && other.Pre[i].All(char.IsAsciiDigit);
            result = leftNumeric && rightNumeric ? left.CompareTo(right)
                : leftNumeric != rightNumeric ? leftNumeric ? -1 : 1
                : string.CompareOrdinal(Pre[i], other.Pre[i]);
            if (result != 0) return result;
        }
        return Pre.Length.CompareTo(other.Pre.Length);
    }
}
