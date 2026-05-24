// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using NPS.Demo.JoplinNoteNode.Client;
using NPS.NWP.Frames;
using NPS.NWP.MemoryNode;

namespace NPS.Demo.JoplinNoteNode.Nodes;

/// <summary>
/// Memory Node exposing Joplin notebooks (folders) as queryable rows.
/// Intended as a graph.ref child of <see cref="JoplinComplexProvider"/>.
///
/// Schema fields: id · title · parent_id · created_time · updated_time
/// </summary>
public sealed class JoplinNotebooksProvider(IJoplinBackend joplin) : IMemoryNodeProvider
{
    public async Task<MemoryNodeQueryResult> QueryAsync(
        QueryFrame        frame,
        MemoryNodeSchema  schema,
        MemoryNodeOptions options,
        CancellationToken ct = default)
    {
        var all = await FetchAllAsync(ct);
        int limit = (int)(frame.Limit == 0 ? options.DefaultLimit : Math.Min(frame.Limit, options.MaxLimit));
        var page  = all.Take(limit).ToList();

        return new MemoryNodeQueryResult { Rows = page };
    }

    public async IAsyncEnumerable<IReadOnlyList<IReadOnlyDictionary<string, object?>>> StreamAsync(
        QueryFrame        frame,
        MemoryNodeSchema  schema,
        MemoryNodeOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        int pg = 1;
        while (true)
        {
            var result = await joplin.ListNotebooksAsync(pg, ct);
            if (result.Items.Length == 0) yield break;
            yield return result.Items.Select(NotebookToRow).ToList();
            if (!result.HasMore) yield break;
            pg++;
        }
    }

    public async Task<long> CountAsync(
        QueryFrame frame, MemoryNodeSchema schema, CancellationToken ct = default)
    {
        var all = await FetchAllAsync(ct);
        return all.Count;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<List<IReadOnlyDictionary<string, object?>>> FetchAllAsync(CancellationToken ct)
    {
        var all = new List<IReadOnlyDictionary<string, object?>>();
        int pg  = 1;
        while (true)
        {
            var result = await joplin.ListNotebooksAsync(pg, ct);
            all.AddRange(result.Items.Select(NotebookToRow));
            if (!result.HasMore) break;
            pg++;
        }
        return all;
    }

    private static IReadOnlyDictionary<string, object?> NotebookToRow(JoplinNotebook nb) =>
        new Dictionary<string, object?>
        {
            ["id"]           = nb.Id,
            ["title"]        = nb.Title,
            ["parent_id"]    = nb.ParentId,
            ["created_time"] = nb.CreatedTime,
            ["updated_time"] = nb.UpdatedTime,
        };
}
