// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using Microsoft.Extensions.Options;

namespace NPS.Demo.JoplinNoteNode.Client;

/// <summary>
/// <see cref="IJoplinBackend"/> backed by the Joplin Web Clipper REST API
/// (default port 41184). Requires Joplin Desktop to be running with the
/// Web Clipper service enabled.
/// </summary>
public sealed class JoplinWebClipperBackend(HttpClient http, IOptions<JoplinClientOptions> opts)
    : IJoplinBackend
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _token = opts.Value.Token;

    // ── Notes ─────────────────────────────────────────────────────────────────

    public async Task<JoplinNote?> GetNoteAsync(string id, CancellationToken ct = default)
    {
        var url = Q($"notes/{id}", ("fields", "id,title,body,parent_id,created_time,updated_time,source_url,is_todo,todo_completed"));
        return await http.GetFromJsonAsync<JoplinNote>(url, JsonOpts, ct);
    }

    public async Task<JoplinPagedResult<JoplinNote>> ListNotesAsync(int page = 1, int limit = 50, CancellationToken ct = default)
    {
        var url = Q("notes",
            ("fields", "id,title,parent_id,created_time,updated_time,source_url"),
            ("page",   page.ToString()),
            ("limit",  limit.ToString()));
        return await http.GetFromJsonAsync<JoplinPagedResult<JoplinNote>>(url, JsonOpts, ct)
               ?? new JoplinPagedResult<JoplinNote> { Items = [] };
    }

    public async Task<JoplinPagedResult<JoplinNote>> SearchNotesAsync(string query, int page = 1, int limit = 20, CancellationToken ct = default)
    {
        var url = Q("search",
            ("query",  query),
            ("type",   "note"),
            ("fields", "id,title,parent_id,created_time,updated_time,source_url"),
            ("page",   page.ToString()),
            ("limit",  limit.ToString()));
        return await http.GetFromJsonAsync<JoplinPagedResult<JoplinNote>>(url, JsonOpts, ct)
               ?? new JoplinPagedResult<JoplinNote> { Items = [] };
    }

    public async Task<JoplinNote> CreateNoteAsync(CreateNoteRequest req, CancellationToken ct = default)
    {
        var resp = await http.PostAsJsonAsync(Q("notes"), req, JsonOpts, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JoplinNote>(JsonOpts, ct))!;
    }

    public async Task<JoplinNote> UpdateNoteAsync(string id, UpdateNoteRequest req, CancellationToken ct = default)
    {
        var resp = await http.PutAsJsonAsync(Q($"notes/{id}"), req, JsonOpts, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JoplinNote>(JsonOpts, ct))!;
    }

    public async Task DeleteNoteAsync(string id, CancellationToken ct = default)
        => (await http.DeleteAsync(Q($"notes/{id}"), ct)).EnsureSuccessStatusCode();

    // ── Notebooks ─────────────────────────────────────────────────────────────

    public async Task<JoplinPagedResult<JoplinNotebook>> ListNotebooksAsync(int page = 1, CancellationToken ct = default)
    {
        var url = Q("folders", ("fields", "id,title,parent_id,created_time,updated_time"), ("page", page.ToString()));
        return await http.GetFromJsonAsync<JoplinPagedResult<JoplinNotebook>>(url, JsonOpts, ct)
               ?? new JoplinPagedResult<JoplinNotebook> { Items = [] };
    }

    public async Task<JoplinNotebook> CreateNotebookAsync(CreateNotebookRequest req, CancellationToken ct = default)
    {
        var resp = await http.PostAsJsonAsync(Q("folders"), req, JsonOpts, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JoplinNotebook>(JsonOpts, ct))!;
    }

    public async Task DeleteNotebookAsync(string id, CancellationToken ct = default)
        => (await http.DeleteAsync(Q($"folders/{id}"), ct)).EnsureSuccessStatusCode();

    // ── Tags ──────────────────────────────────────────────────────────────────

    public async Task<JoplinTag[]> GetNoteTagsAsync(string noteId, CancellationToken ct = default)
    {
        var result = await http.GetFromJsonAsync<JoplinPagedResult<JoplinTag>>(
            Q($"notes/{noteId}/tags", ("fields", "id,title")), JsonOpts, ct);
        return result?.Items ?? [];
    }

    public async Task<JoplinTag> CreateTagAsync(string title, CancellationToken ct = default)
    {
        var resp = await http.PostAsJsonAsync(Q("tags"), new { title }, JsonOpts, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JoplinTag>(JsonOpts, ct))!;
    }

    public async Task AddTagToNoteAsync(string tagId, string noteId, CancellationToken ct = default)
        => (await http.PostAsJsonAsync(Q($"tags/{tagId}/notes"), new { id = noteId }, JsonOpts, ct)).EnsureSuccessStatusCode();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string Q(string path, params (string key, string value)[] queryParams)
    {
        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["token"] = _token;
        foreach (var (k, v) in queryParams) qs[k] = v;
        return $"{path}?{qs}";
    }
}
