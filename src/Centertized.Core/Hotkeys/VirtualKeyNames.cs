namespace Centertized.Core.Hotkeys;

/// <summary>
/// Conversion of a Win32 virtual-key code to a readable name and back, with no dependency on the WPF
/// <c>Key</c> enum. Covers the common keys that make sense as a global
/// shortcut (letters, digits, function keys, a few named keys).
/// </summary>
internal static class VirtualKeyNames
{
    private static readonly Dictionary<uint, string> NamedKeys = new()
    {
        [0x08] = "Backspace",
        [0x09] = "Tab",
        [0x0D] = "Enter",
        [0x1B] = "Escape",
        [0x20] = "Space",
        [0x21] = "PageUp",
        [0x22] = "PageDown",
        [0x23] = "End",
        [0x24] = "Home",
        [0x25] = "Left",
        [0x26] = "Up",
        [0x27] = "Right",
        [0x28] = "Down",
        [0x2C] = "PrintScreen",
        [0x2D] = "Insert",
        [0x2E] = "Delete",
    };

    public static string ToDisplayName(uint virtualKeyCode)
    {
        // VK_0..VK_9 (0x30-0x39) and VK_A..VK_Z (0x41-0x5A) map directly to ASCII,
        // so they can simply be cast to char.
        if (virtualKeyCode is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A)
        {
            return ((char)virtualKeyCode).ToString();
        }

        // VK_F1..VK_F24
        if (virtualKeyCode is >= 0x70 and <= 0x87)
        {
            return $"F{virtualKeyCode - 0x70 + 1}";
        }

        if (NamedKeys.TryGetValue(virtualKeyCode, out var name))
        {
            return name;
        }

        return $"VK{virtualKeyCode:X2}";
    }

    public static bool TryParseDisplayName(string name, out uint virtualKeyCode)
    {
        virtualKeyCode = 0;

        if (name.Length == 1)
        {
            var c = char.ToUpperInvariant(name[0]);
            if (c is >= '0' and <= '9' or >= 'A' and <= 'Z')
            {
                virtualKeyCode = c;
                return true;
            }
        }

        if (name.Length is 2 or 3 && name[0] is 'F' or 'f' && int.TryParse(name.AsSpan(1), out var fNumber) && fNumber is >= 1 and <= 24)
        {
            virtualKeyCode = (uint)(0x70 + fNumber - 1);
            return true;
        }

        foreach (var (code, namedValue) in NamedKeys)
        {
            if (string.Equals(namedValue, name, StringComparison.OrdinalIgnoreCase))
            {
                virtualKeyCode = code;
                return true;
            }
        }

        if (name.StartsWith("VK", StringComparison.OrdinalIgnoreCase) &&
            uint.TryParse(name.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out var parsed))
        {
            virtualKeyCode = parsed;
            return true;
        }

        return false;
    }
}
