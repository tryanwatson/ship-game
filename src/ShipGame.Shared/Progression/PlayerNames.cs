namespace ShipGame.Shared.Progression;

/// <summary>What players may call themselves: shown over their ship and on the map, in the pixel font's capitals.</summary>
public static class PlayerNames
{
    public const int MaxLength = 14;

    /// <summary>A name for players who haven't given one (solo, before typing anything).</summary>
    public const string Default = "CAPTAIN";

    /// <summary>
    /// <paramref name="name"/> as it will be shown: capitals, digits and single spaces only, at most
    /// <see cref="MaxLength"/> long. Empty if nothing usable is left.
    /// </summary>
    public static string Clean(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return "";
        var kept = new System.Text.StringBuilder(MaxLength);
        foreach (var c in name.ToUpperInvariant())
        {
            if (kept.Length >= MaxLength)
                break;
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
                kept.Append(c);
            else if (c == ' ' && kept.Length > 0 && kept[^1] != ' ')
                kept.Append(c);
        }
        return kept.ToString().TrimEnd();
    }

    /// <summary>Whether a character can go in a name (for text entry).</summary>
    public static bool Allows(char c) => char.IsAsciiLetterOrDigit(c) || c == ' ';
}
