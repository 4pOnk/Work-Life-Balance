namespace WorkLifeBalance.Windows;

public sealed record Hotkey(uint Modifiers, uint Key)
{
    public static Hotkey Parse(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        uint modifiers = 0;
        if (parts.Length < 2) throw new ArgumentException("Добавьте Ctrl, Alt или Win к клавише.");
        foreach (var part in parts[..^1])
        {
            var modifier = part.ToUpperInvariant() switch
            {
                "ALT" => 1u,
                "CTRL" => 2u,
                "SHIFT" => 4u,
                "WIN" => 8u,
                _ => throw new ArgumentException("Модификаторы: Ctrl, Alt, Shift, Win.")
            };
            if ((modifiers & modifier) != 0) throw new ArgumentException("Модификатор указан дважды.");
            modifiers |= modifier;
        }
        if ((modifiers & 11) == 0) throw new ArgumentException("Добавьте Ctrl, Alt или Win.");
        var key = parts[^1].ToUpperInvariant();
        uint code;
        if (key.Length == 1 && (key[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')) code = key[0];
        else if (key.StartsWith('F') && int.TryParse(key[1..], out var number) && number is >= 1 and <= 24)
            code = (uint)(111 + number);
        else throw new ArgumentException("Клавиша: A–Z, 0–9 или F1–F24.");
        return new(modifiers | 0x4000, code);
    }
}
