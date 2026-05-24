// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NPS.Demo.JoplinNoteNode.Client;
using NPS.NWP.ActionNode;
using NPS.NWP.ComplexNode;
using NPS.NWP.Frames;
using NPS.NWP.MemoryNode;

namespace NPS.Demo.JoplinNoteNode.Nodes;

/// <summary>
/// Complex Node provider backed by Joplin notes.
///
/// Memory layer: note metadata (id, title, parent_id, snippet, source_url, tags, timestamps).
/// graph.refs:
///   "content"   → /content   (JoplinContentProvider — full note body)
///   "notebooks" → /notebooks (JoplinNotebooksProvider — notebook tree)
///
/// Action layer: all CRUD operations (delegates to JoplinActionProvider).
/// </summary>
public sealed class JoplinComplexProvider(IJoplinBackend joplin, JoplinActionProvider actions)
    : IComplexNodeProvider
{
    private const int SnippetLength = 200;

    public async Task<MemoryNodeQueryResult> QueryAsync(
        QueryFrame         frame,
        ComplexNodeOptions options,
        CancellationToken  ct = default)
    {
        int page  = DecodeCursor(frame.Cursor);
        int limit = (int)(frame.Limit == 0 ? 20 : Math.Min(frame.Limit, 200));

        JoplinPagedResult<JoplinNote> result;

        string? searchQuery = ExtractSearchQuery(frame.Filter);
        if (searchQuery is not null)
            result = await joplin.SearchNotesAsync(searchQuery, page, limit, ct);
        else
            result = await joplin.ListNotesAsync(page, limit, ct);

        var rows = result.Items.Select(NoteToRow).ToList();
        return new MemoryNodeQueryResult
        {
            Rows       = rows,
            NextCursor = result.HasMore ? EncodeCursor(page + 1) : null,
        };
    }

    public Task<ActionExecutionResult> ExecuteAsync(
        ActionFrame frame, ActionContext context, CancellationToken ct = default)
        => actions.ExecuteAsync(frame, context, ct);

    // ── Row mapping ───────────────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, object?> NoteToRow(JoplinNote n) =>
        new Dictionary<string, object?>
        {
            ["id"]           = n.Id,
            ["title"]        = n.Title,
            ["parent_id"]    = n.ParentId,
            ["snippet"]      = n.Body.Length <= SnippetLength
                                   ? n.Body
                                   : n.Body[..SnippetLength] + "…",
            ["source_url"]   = n.SourceUrl,
            ["is_todo"]      = n.IsTodo,
            ["created_time"] = n.CreatedTime,
            ["updated_time"] = n.UpdatedTime,
        };

    // ── Helpers (same as JoplinMemoryProvider) ────────────────────────────────

    private static string? ExtractSearchQuery(JsonElement? filter)
    {
        if (filter is null) return null;
        var f = filter.Value;
        if (f.ValueKind != JsonValueKind.Object) return null;

        foreach (var prop in f.EnumerateObject())
        {
            if (prop.Name is "title" or "body" or "snippet" && prop.Value.ValueKind == JsonValueKind.Object)
            {
                if (prop.Value.TryGetProperty("$contains", out var val) && val.ValueKind == JsonValueKind.String)
                    return val.GetString();
            }
            if (prop.Name == "$or" && prop.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var elem in prop.Value.EnumerateArray())
                {
                    var q = ExtractSearchQuery(elem);
                    if (q is not null) return q;
                }
            }
        }
        return null;
    }

    private static int DecodeCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return 1;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(
                cursor.Replace('-', '+').Replace('_', '/')));
            if (decoded.StartsWith("page=") && int.TryParse(decoded[5..], out int p)) return p;
        }
        catch { /* ignore */ }
        return 1;
    }

    private static string EncodeCursor(int page) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"page={page}"))
               .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
