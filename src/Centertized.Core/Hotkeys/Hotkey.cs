using System.Text;

namespace Centertized.Core.Hotkeys;

/// <summary>
/// Kombinace modifikátorů + jedné klávesy. Kanonický textový formát (pro ukládání
/// do settings.json i zobrazení v UI) je vždy "Ctrl+Alt+Shift+Win+Klávesa" v tomhle
/// pevném pořadí, takže porovnávání stringů je spolehlivé.
/// </summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, uint VirtualKeyCode)
{
    public override string ToString()
    {
        // Lokální funkce ve struct metodě nesmí zachytávat "this", proto modifiers
        // jako explicitní parametr místo čtení Modifiers přímo.
        var modifiers = Modifiers;
        var sb = new StringBuilder();
        AppendIfSet(sb, modifiers, HotkeyModifiers.Control, "Ctrl");
        AppendIfSet(sb, modifiers, HotkeyModifiers.Alt, "Alt");
        AppendIfSet(sb, modifiers, HotkeyModifiers.Shift, "Shift");
        AppendIfSet(sb, modifiers, HotkeyModifiers.Windows, "Win");

        if (sb.Length > 0)
        {
            sb.Append('+');
        }

        sb.Append(VirtualKeyNames.ToDisplayName(VirtualKeyCode));
        return sb.ToString();

        static void AppendIfSet(StringBuilder builder, HotkeyModifiers modifiers, HotkeyModifiers flag, string label)
        {
            if (!modifiers.HasFlag(flag))
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append('+');
            }

            builder.Append(label);
        }
    }

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            // Potřebujeme aspoň jeden modifikátor a jednu klávesu.
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var modifier = parts[i].ToLowerInvariant() switch
            {
                "ctrl" or "control" => HotkeyModifiers.Control,
                "alt" => HotkeyModifiers.Alt,
                "shift" => HotkeyModifiers.Shift,
                "win" or "windows" => HotkeyModifiers.Windows,
                _ => (HotkeyModifiers?)null,
            };

            if (modifier is null)
            {
                return false;
            }

            modifiers |= modifier.Value;
        }

        if (modifiers == HotkeyModifiers.None)
        {
            return false;
        }

        if (!VirtualKeyNames.TryParseDisplayName(parts[^1], out var virtualKeyCode))
        {
            return false;
        }

        hotkey = new Hotkey(modifiers, virtualKeyCode);
        return true;
    }
}
