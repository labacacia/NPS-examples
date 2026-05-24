// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

namespace NPS.Demo.JoplinNoteNode.Client;

/// <summary>
/// Serializes and deserializes Joplin items using the plain-text format used by the
/// Joplin sync engine (equivalent to BaseItem.serialize / BaseItem.unserialize in
/// the official Joplin TypeScript codebase).
///
/// Wire format (type_=1 note example):
/// <code>
///   My Title
///
///   Body text here.
///   Can span multiple lines.
///
///   id: 83cd4fde618744ec98a1409e7dd6cfc0
///   parent_id: b7f6c16d338b419faddb013a53db1663
///   created_time: 2026-05-21T10:00:00.000Z
///   updated_time: 2026-05-21T10:00:00.000Z
///   is_todo: 0
///   type_: 1
/// </code>
///
/// Parsing rules (bottom-up):
///   1. Scan lines from end; lines until first blank line = props block.
///   2. Remaining lines: first line = title, blank line, rest = body.
///   3. Body is stored raw (no escaping). All other prop values use \n → \\n escaping.
/// </summary>
public static class JoplinItemSerializer
{
    public const int TypeNote     = 1;
    public const int TypeNotebook = 2;
    public const int TypeTag      = 5;
    public const int TypeNoteTag  = 6;

    // ── Serialize ─────────────────────────────────────────────────────────────

    public static string SerializeNote(JoplinNote note)
    {
        var props = new Dictionary<string, string>
        {
            ["id"]                   = note.Id,
            ["parent_id"]            = note.ParentId ?? string.Empty,
            ["created_time"]         = FormatTimestamp(note.CreatedTime),
            ["updated_time"]         = FormatTimestamp(note.UpdatedTime),
            ["is_conflict"]          = "0",
            ["latitude"]             = "0.00000000",
            ["longitude"]            = "0.00000000",
            ["altitude"]             = "0.0000",
            ["author"]               = string.Empty,
            ["source_url"]           = note.SourceUrl ?? string.Empty,
            ["is_todo"]              = note.IsTodo.ToString(),
            ["todo_due"]             = "0",
            ["todo_completed"]       = note.TodoCompleted.ToString(),
            ["source"]               = "joplinapp.org",
            ["source_application"]   = "net.cozic.joplin-desktop",
            ["application_data"]     = string.Empty,
            ["order"]                = "0",
            ["user_created_time"]    = FormatTimestamp(note.CreatedTime),
            ["user_updated_time"]    = FormatTimestamp(note.UpdatedTime),
            ["encryption_cipher_text"] = string.Empty,
            ["encryption_applied"]   = "0",
            ["markup_language"]      = "1",
            ["is_shared"]            = "0",
            ["share_id"]             = string.Empty,
            ["conflict_original_id"] = string.Empty,
            ["master_key_id"]        = string.Empty,
            ["type_"]                = TypeNote.ToString(),
        };

        return Build(note.Title, note.Body, props);
    }

    public static string SerializeNotebook(JoplinNotebook nb)
    {
        var props = new Dictionary<string, string>
        {
            ["id"]                     = nb.Id,
            ["parent_id"]              = nb.ParentId ?? string.Empty,
            ["created_time"]           = FormatTimestamp(nb.CreatedTime),
            ["updated_time"]           = FormatTimestamp(nb.UpdatedTime),
            ["user_created_time"]      = FormatTimestamp(nb.CreatedTime),
            ["user_updated_time"]      = FormatTimestamp(nb.UpdatedTime),
            ["encryption_cipher_text"] = string.Empty,
            ["encryption_applied"]     = "0",
            ["is_shared"]              = "0",
            ["share_id"]               = string.Empty,
            ["icon"]                   = string.Empty,
            ["type_"]                  = TypeNotebook.ToString(),
        };

        return Build(nb.Title, body: null, props);
    }

    // ── Deserialize ───────────────────────────────────────────────────────────

    public static JoplinNote DeserializeNote(string text)
    {
        if (string.IsNullOrEmpty(text)) throw new ArgumentException("Item text is empty.");

        Parse(text, out string title, out string body, out var props);

        AssertType(props, TypeNote);
        if (props.TryGetValue("encryption_applied", out var enc) && enc == "1")
            throw new InvalidOperationException("Item is encrypted; cannot deserialize without the encryption key.");

        return new JoplinNote
        {
            Id            = props.GetValueOrDefault("id", string.Empty),
            Title         = title,
            Body          = body,
            ParentId      = props.GetValueOrDefault("parent_id", string.Empty),
            CreatedTime   = ParseTimestamp(props.GetValueOrDefault("created_time")),
            UpdatedTime   = ParseTimestamp(props.GetValueOrDefault("updated_time")),
            IsTodo        = int.TryParse(props.GetValueOrDefault("is_todo"), out int t) ? t : 0,
            TodoCompleted = int.TryParse(props.GetValueOrDefault("todo_completed"), out int tc) ? tc : 0,
            SourceUrl     = props.GetValueOrDefault("source_url"),
        };
    }

    public static JoplinNotebook DeserializeNotebook(string text)
    {
        if (string.IsNullOrEmpty(text)) throw new ArgumentException("Item text is empty.");

        Parse(text, out string title, out _, out var props);

        AssertType(props, TypeNotebook);

        return new JoplinNotebook
        {
            Id          = props.GetValueOrDefault("id", string.Empty),
            Title       = title,
            ParentId    = props.GetValueOrDefault("parent_id", string.Empty),
            CreatedTime = ParseTimestamp(props.GetValueOrDefault("created_time")),
            UpdatedTime = ParseTimestamp(props.GetValueOrDefault("updated_time")),
        };
    }

    /// <summary>
    /// Detects the item type from the <c>type_</c> prop without full deserialization.
    /// Returns -1 if the text is not a valid Joplin item.
    /// </summary>
    public static int DetectType(string text)
    {
        if (string.IsNullOrEmpty(text)) return -1;
        Parse(text, out _, out _, out var props);
        return int.TryParse(props.GetValueOrDefault("type_"), out int t) ? t : -1;
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private static string Build(string title, string? body, Dictionary<string, string> props)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine(title ?? string.Empty);

        if (!string.IsNullOrEmpty(body))
        {
            sb.AppendLine();
            sb.AppendLine(body);
        }

        sb.AppendLine();

        foreach (var (key, value) in props)
        {
            // body is written above; skip if accidentally included
            if (key == "body") continue;
            sb.AppendLine($"{key}: {EscapePropValue(value)}");
        }

        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static void Parse(string text, out string title, out string body, out Dictionary<string, string> props)
    {
        var lines = text.Split('\n');

        props = new Dictionary<string, string>(StringComparer.Ordinal);
        int propsEnd = lines.Length;

        // Scan from the bottom; collect props until we hit a blank line.
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].TrimEnd('\r');

            if (line.Length == 0)
            {
                propsEnd = i;
                break;
            }

            int colon = line.IndexOf(':');
            if (colon < 0)
            {
                // Not a prop line — treat everything above (inclusive) as body.
                propsEnd = i + 1;
                break;
            }

            var key = line[..colon].Trim();
            var val = line[(colon + 1)..].Trim();
            props[key] = UnescapePropValue(key, val);
        }

        // Everything above propsEnd is title + body block.
        var bodyLines = lines[..propsEnd];

        // Trim trailing blank lines from body block.
        int bodyEnd = bodyLines.Length;
        while (bodyEnd > 0 && bodyLines[bodyEnd - 1].TrimEnd('\r').Length == 0)
            bodyEnd--;

        bodyLines = bodyLines[..bodyEnd];

        if (bodyLines.Length == 0)
        {
            title = string.Empty;
            body  = string.Empty;
            return;
        }

        title = bodyLines[0].TrimEnd('\r');

        // Skip blank line after title.
        int bodyStart = 1;
        if (bodyStart < bodyLines.Length && bodyLines[bodyStart].TrimEnd('\r').Length == 0)
            bodyStart++;

        body = bodyStart < bodyLines.Length
            ? string.Join("\n", bodyLines[bodyStart..].Select(l => l.TrimEnd('\r')))
            : string.Empty;
    }

    private static void AssertType(Dictionary<string, string> props, int expected)
    {
        if (!props.TryGetValue("type_", out var typeStr) || !int.TryParse(typeStr, out int actual))
            throw new InvalidOperationException("Item is missing type_.");
        if (actual != expected)
            throw new InvalidOperationException($"Expected type_ {expected} but got {actual}.");
    }

    // ── Prop value escaping (mirrors BaseItem.serialize_format) ───────────────

    private static string EscapePropValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        // Order matters: escape literal backslash sequences first
        return value
            .Replace("\\n", "\\\\n")
            .Replace("\\r", "\\\\r")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
    }

    private static string UnescapePropValue(string key, string value)
    {
        if (string.IsNullOrEmpty(value) || key == "body") return value;

        return value
            .Replace("\\\\n", "\x00NL\x00")
            .Replace("\\\\r", "\x00CR\x00")
            .Replace("\\n", "\n")
            .Replace("\\r", "\r")
            .Replace("\x00NL\x00", "\\n")
            .Replace("\x00CR\x00", "\\r");
    }

    // ── Timestamp helpers ─────────────────────────────────────────────────────

    public static string FormatTimestamp(long unixMs)
    {
        if (unixMs == 0) return string.Empty;
        return DateTimeOffset.FromUnixTimeMilliseconds(unixMs)
                             .UtcDateTime
                             .ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    }

    public static long ParseTimestamp(string? value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        if (DateTimeOffset.TryParse(value, out var dto))
            return dto.ToUnixTimeMilliseconds();
        return 0;
    }
}
