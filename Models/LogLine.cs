using System.Windows.Media;

namespace LocalRack.Models;

public sealed class LogSegment
{
    public LogSegment(string text, Brush? foreground = null, Brush? background = null,
        bool bold = false, bool italic = false, bool underline = false)
    {
        Text = text;
        Foreground = foreground;
        Background = background;
        Bold = bold;
        Italic = italic;
        Underline = underline;
    }

    public string Text { get; }
    public Brush? Foreground { get; }
    public Brush? Background { get; }
    public bool Bold { get; }
    public bool Italic { get; }
    public bool Underline { get; }
}

// Deliberately a class, not a record: identical log lines must stay distinct items.
public sealed class LogLine
{
    public LogLine(string text, bool isError)
        : this(text, isError, new[] { new LogSegment(text) })
    {
    }

    public LogLine(string text, bool isError, IReadOnlyList<LogSegment> segments)
    {
        Text = text;
        IsError = isError;
        Segments = segments;
    }

    /// <summary>Plain text with all escape codes removed.</summary>
    public string Text { get; }
    public bool IsError { get; }
    public IReadOnlyList<LogSegment> Segments { get; }
}
