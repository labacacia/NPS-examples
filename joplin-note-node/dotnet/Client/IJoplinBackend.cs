// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

namespace NPS.Demo.JoplinNoteNode.Client;

/// <summary>
/// Abstraction over Joplin data access. Two implementations are provided:
/// <list type="bullet">
///   <item><see cref="JoplinWebClipperBackend"/> — talks to the local Web Clipper
///   HTTP API (port 41184). Requires Joplin Desktop to be running.</item>
///   <item><see cref="JoplinServerBackend"/> — talks directly to a self-hosted
///   Joplin Server instance. No Joplin Desktop required.</item>
/// </list>
/// </summary>
public interface IJoplinBackend
{
    // ── Notes ─────────────────────────────────────────────────────────────────

    Task<JoplinNote?> GetNoteAsync(string id, CancellationToken ct = default);

    Task<JoplinPagedResult<JoplinNote>> ListNotesAsync(int page, int limit, CancellationToken ct = default);

    Task<JoplinPagedResult<JoplinNote>> SearchNotesAsync(string query, int page, int limit, CancellationToken ct = default);

    Task<JoplinNote> CreateNoteAsync(CreateNoteRequest req, CancellationToken ct = default);

    Task<JoplinNote> UpdateNoteAsync(string id, UpdateNoteRequest req, CancellationToken ct = default);

    Task DeleteNoteAsync(string id, CancellationToken ct = default);

    // ── Notebooks ─────────────────────────────────────────────────────────────

    Task<JoplinPagedResult<JoplinNotebook>> ListNotebooksAsync(int page, CancellationToken ct = default);

    Task<JoplinNotebook> CreateNotebookAsync(CreateNotebookRequest req, CancellationToken ct = default);

    Task DeleteNotebookAsync(string id, CancellationToken ct = default);

    // ── Tags ──────────────────────────────────────────────────────────────────

    Task<JoplinTag[]> GetNoteTagsAsync(string noteId, CancellationToken ct = default);

    Task<JoplinTag> CreateTagAsync(string title, CancellationToken ct = default);

    Task AddTagToNoteAsync(string tagId, string noteId, CancellationToken ct = default);
}
