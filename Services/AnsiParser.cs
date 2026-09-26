using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using LocalRack.Models;

namespace LocalRack.Services;

/// <summary>
/// Converts terminal output containing ANSI escape codes into styled segments.
/// SGR (color/style) codes are interpreted; every other escape sequence (cursor movement,
/// window titles, hyperlinks) is dropped. Style state carries across lines, as in a real
/// terminal, so use one parser per output stream. Not thread-safe; each stream's callbacks
/// arrive sequentially.
/// </summary>
public sealed partial class AnsiParser
{
    // VS Code "Light" terminal palette: the standard 16 colors tuned to stay readable on a light background.
    private static readonly Color[] Palette =
    {
        C(0x00, 0x00, 0x00), C(0xCD, 0x31, 0x31), C(0x00, 0x9A, 0x00), C(0x94, 0x98, 0x00),
        C(0x04, 0x51, 0xA5), C(0xBC, 0x05, 0xBC), C(0x05, 0x98, 0xBC), C(0x55, 0x55, 0x55),
        C(0x66, 0x66, 0x66), C(0xCD, 0x31, 0x31), C(0x14, 0xB0, 0x14), C(0xB5, 0xBA, 0x00),
        C(0x04, 0x51, 0xA5), C(0xBC, 0x05, 0xBC), C(0x05, 0x98, 0xBC), C(0x77, 0x77, 0x77),
    };

    private static readonly ConcurrentDictionary<(Color Color, bool Dim), Brush> BrushCache = new();

    private Color? _fg;
    private Color? _bg;
    private bool _bold;
    private bool _dim;
    private bool _italic;
    private bool _underline;
    private bool _inverse;

    public LogLine Parse(string raw, bool isError, string? prefix, Color? defaultForeground)
    {
        var segments = new List<LogSegment>();
        var plain = new StringBuilder();

        if (prefix is not null)
        {
            segments.Add(new LogSegment(prefix, defaultForeground is { } p ? GetBrush(p, false) : null));
            plain.Append(prefix);
        }

        var position = 0;
        foreach (Match match in EscapePattern().Matches(raw))
        {
            AddText(raw[position..match.Index], segments, plain, defaultForeground);
            if (match.Groups["sgr"].Success)
            {
                ApplySgr(match.Groups["sgr"].Value);
            }
            position = match.Index + match.Length;
        }
        AddText(raw[position..], segments, plain, defaultForeground);

        if (segments.Count == 0)
        {
            segments.Add(new LogSegment(string.Empty));
        }
        return new LogLine(plain.ToString(), isError, segments);
    }

    public static string StripAnsi(string raw) => EscapePattern().Replace(raw, string.Empty);

    private void AddText(string text, List<LogSegment> segments, StringBuilder plain, Color? defaultForeground)
    {
        // Drop stray control characters (e.g. \r from progress output, bell) that render as garbage.
        text = ControlCharPattern().Replace(text, string.Empty);
        if (text.Length == 0) return;
        plain.Append(text);

        var fg = _fg ?? defaultForeground;
        var bg = _bg;
        if (_inverse)
        {
            (fg, bg) = (bg ?? C(0xFD, 0xFD, 0xFB), fg ?? C(0x1F, 0x1F, 0x1F));
        }

        segments.Add(new LogSegment(
            text,
            fg is { } f ? GetBrush(Readable(f), _dim) : (_dim ? GetBrush(C(0x1F, 0x1F, 0x1F), true) : null),
            bg is { } b ? GetBrush(b, false) : null,
            _bold,
            _italic,
            _underline));
    }

    private void ApplySgr(string parameters)
    {
        var codes = parameters.Length == 0
            ? new[] { 0 }
            : parameters.Split(';', ':').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();

        for (var i = 0; i < codes.Length; i++)
        {
            var code = codes[i];
            switch (code)
            {
                case 0: Reset(); break;
                case 1: _bold = true; break;
                case 2: _dim = true; break;
                case 3: _italic = true; break;
                case 4: _underline = true; break;
                case 7: _inverse = true; break;
                case 21: _underline = true; break;
                case 22: _bold = false; _dim = false; break;
                case 23: _italic = false; break;
                case 24: _underline = false; break;
                case 27: _inverse = false; break;
                case >= 30 and <= 37: _fg = Palette[code - 30]; break;
                case 38: _fg = ReadExtendedColor(codes, ref i); break;
                case 39: _fg = null; break;
                case >= 40 and <= 47: _bg = Palette[code - 40]; break;
                case 48: _bg = ReadExtendedColor(codes, ref i); break;
                case 49: _bg = null; break;
                case >= 90 and <= 97: _fg = Palette[code - 90 + 8]; break;
                case >= 100 and <= 107: _bg = Palette[code - 100 + 8]; break;
            }
        }
    }

    private static Color? ReadExtendedColor(int[] codes, ref int i)
    {
        if (i + 1 >= codes.Length) return null;
        var mode = codes[i + 1];
        if (mode == 5 && i + 2 < codes.Length)
        {
            var index = codes[i + 2];
            i += 2;
            return From256(index);
        }
        if (mode == 2 && i + 4 < codes.Length)
        {
            var color = C((byte)codes[i + 2], (byte)codes[i + 3], (byte)codes[i + 4]);
            i += 4;
            return color;
        }
        return null;
    }

    private static Color From256(int index)
    {
        if (index < 16) return Palette[Math.Clamp(index, 0, 15)];
        if (index < 232)
        {
            index -= 16;
            static byte Level(int v) => (byte)(v == 0 ? 0 : 55 + v * 40);
            return C(Level(index / 36), Level(index / 6 % 6), Level(index % 6));
        }
        var gray = (byte)(8 + (Math.Min(index, 255) - 232) * 10);
        return C(gray, gray, gray);
    }

    /// <summary>Darkens colors that would be nearly invisible on the light console background.</summary>
    private static Color Readable(Color c)
    {
        var luminance = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
        if (luminance <= 0.7) return c;
        var factor = 0.7 / luminance;
        return C((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));
    }

    private static Brush GetBrush(Color color, bool dim) =>
        BrushCache.GetOrAdd((color, dim), key =>
        {
            var c = key.Color;
            if (key.Dim) c = Color.FromArgb(0x99, c.R, c.G, c.B);
            var brush = new SolidColorBrush(c);
            brush.Freeze(); // created off the UI thread; frozen brushes are shareable across threads
            return brush;
        });

    private void Reset()
    {
        _fg = null;
        _bg = null;
        _bold = false;
        _dim = false;
        _italic = false;
        _underline = false;
        _inverse = false;
    }

    private static Color C(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    // CSI sequences (SGR captured when final byte is 'm'), OSC sequences, and two-byte escapes.
    [GeneratedRegex(@"\x1B\[(?:(?<sgr>[0-9;:]*)m|[0-?]*[ -/]*[@-~])|\x1B\][^\x07\x1B]*(?:\x07|\x1B\\)|\x1B[@-Z\\-_]")]
    private static partial Regex EscapePattern();

    [GeneratedRegex(@"[\x00-\x08\x0B-\x1F\x7F]")]
    private static partial Regex ControlCharPattern();
}
