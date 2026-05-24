// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using System.Text.Json;
using NPS.Demo.JoplinNoteNode.Client;
using NPS.NWP.Frames;
using NPS.NWP.MemoryNode;

namespace NPS.Demo.JoplinNoteNode.Nodes;

/// <summary>
/// Memory Node serving full note bodies. Intended as a graph.ref child of
/// <see cref="JoplinComplexProvider"/> expanded at depth≥1.
///
/// Schema fields: note_id · title · body · word_count · source_url
///
/// Typical usage:
///   filter: { "note_id": { "$eq": "abc123" } }   → single note's body
///   filter: { "title": { "$contains": "NPS" } }   → search and return full text
/// </summary>
public sealed class JoplinContentProvider(IJoplinBackend joplin) : IMemoryNodeProvider
{
    public async Task<MemoryNodeQueryResult> QueryAsync(
        QueryFrame        frame,
        MemoryNodeSchema  schema,
        MemoryNodeOptions options,
        CancellationToken ct = default)
    {
        int page  = 1;
        int limit = (int)(frame.Limit == 0 ? options.DefaultLimit : Math.Min(frame.Limit, options.MaxLimit));

        // Fast path: single note by id
        string? noteId = ExtractEqFilter(frame.Filter, "note_id");
        if (noteId is not null)
        {
            var note = await joplin.GetNoteAsync(noteId, ct);
            var rows = note is null
                ? []
                : new List<IReadOnlyDictionary<string, object?>> { NoteToRow(note) };
            return new MemoryNodeQueryResult { Rows = rows };
        }

        // Full-text or listing path
        string? searchQuery = ExtractContainsFilter(frame.Filter, "title")
                           ?? ExtractContainsFilter(frame.Filter, "body");

        JoplinPagedResult<JoplinNote> result = searchQuery is not null
            ? await joplin.SearchNotesAsync(searchQuery, page, limit, ct)
            : await joplin.ListNotesAsync(page, limit, ct);

        // For content node we need full body — fetch each note individually if search
        // returned metadata-only results (Web Clipper search omits body).
        var fullNotes = new List<JoplinNote>();
        foreach (var n in result.Items)
        {
            if (!string.IsNullOrEmpty(n.Body))
            {
                fullNotes.Add(n);
            }
            else
            {
                var full = await joplin.GetNoteAsync(n.Id, ct);
                if (full is not null) fullNotes.Add(full);
            }
        }

        return new MemoryNodeQueryResult
        {
            Rows = fullNotes.Select(NoteToRow).ToList(),
            NextCursor = result.HasMore ? EncodePageCursor(page + 1) : null,
        };
    }

    public async IAsyncEnumerable<IReadOnlyList<IReadOnlyDictionary<string, object?>>> StreamAsync(
        QueryFrame        frame,
        MemoryNodeSchema  schema,
        MemoryNodeOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        int page  = 1;
        int limit = (int)(frame.Limit == 0 ? options.DefaultLimit : Math.Min(frame.Limit, options.MaxLimit));

        while (true)
        {
            var result = await joplin.ListNotesAsync(page, limit, ct);
            if (result.Items.Length == 0) yield break;

            var rows = new List<IReadOnlyDictionary<string, object?>>();
            foreach (var n in result.Items)
            {
                var full = string.IsNullOrEmpty(n.Body) ? await joplin.GetNoteAsync(n.Id, ct) : n;
                if (full is not null) rows.Add(NoteToRow(full));
            }

            yield return rows;
            if (!result.HasMore) yield break;
            page++;
        }
    }

    public async Task<long> CountAsync(
        QueryFrame frame, MemoryNodeSchema schema, CancellationToken ct = default)
    {
        long count = 0;
        int page = 1;
        while (true)
        {
            var result = await joplin.ListNotesAsync(page, 50, ct);
            count += result.Items.Length;
            if (!result.HasMore) break;
            page++;
        }
        return count;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, object?> NoteToRow(JoplinNote n) =>
        new Dictionary<string, object?>
        {
            ["note_id"]    = n.Id,
            ["title"]      = n.Title,
            ["body"]       = n.Body,
            ["word_count"] = CountWords(n.Body),
            ["source_url"] = n.SourceUrl,
        };

    private static int CountWords(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0
            : text.Split([' ', '\n', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;

    private static string? ExtractEqFilter(JsonElement? filter, string field)
    {
        if (filter is null || filter.Value.ValueKind != JsonValueKind.Object) return null;
        foreach (var prop in filter.Value.EnumerateObject())
        {
            if (prop.Name == field && prop.Value.ValueKind == JsonValueKind.Object
                && prop.Value.TryGetProperty("$eq", out var val)
                && val.ValueKind == JsonValueKind.String)
                return val.GetString();
        }
        return null;
    }

    private static string? ExtractContainsFilter(JsonElement? filter, string field)
    {
        if (filter is null || filter.Value.ValueKind != JsonValueKind.Object) return null;
        foreach (var prop in filter.Value.EnumerateObject())
        {
            if (prop.Name == field && prop.Value.ValueKind == JsonValueKind.Object
                && prop.Value.TryGetProperty("$contains", out var val)
                && val.ValueKind == JsonValueKind.String)
                return val.GetString();
        }
        return null;
    }

    private static string EncodePageCursor(int page) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"page={page}"))
               .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
