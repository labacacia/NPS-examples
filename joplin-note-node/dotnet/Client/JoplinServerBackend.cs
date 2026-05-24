// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace NPS.Demo.JoplinNoteNode.Client;

/// <summary>
/// <see cref="IJoplinBackend"/> backed by a self-hosted Joplin Server
/// (https://github.com/laurent22/joplin/tree/dev/packages/server).
/// Does NOT require Joplin Desktop. Any Joplin client pointed at the same
/// server will automatically receive notes written through this backend.
///
/// Auth: POST /api/sessions → session token sent as X-Api-Auth header.
/// Items: text/plain Joplin item format, serialized by <see cref="JoplinItemSerializer"/>.
/// </summary>
public sealed class JoplinServerBackend : IJoplinBackend, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly JoplinServerOptions _opts;
    private string? _sessionToken;
    private readonly SemaphoreSlim _authLock = new(1, 1);

    public JoplinServerBackend(HttpClient http, IOptions<JoplinServerOptions> opts)
    {
        _http = http;
        _opts = opts.Value;
        _http.BaseAddress = new Uri(_opts.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout     = _opts.Timeout;
    }

    // ── Notes ─────────────────────────────────────────────────────────────────

    public async Task<JoplinNote?> GetNoteAsync(string id, CancellationToken ct = default)
    {
        var text = await GetItemContentAsync($"{id}.md", ct);
        if (text is null) return null;
        return JoplinItemSerializer.DetectType(text) == JoplinItemSerializer.TypeNote
            ? JoplinItemSerializer.DeserializeNote(text)
            : null;
    }

    public async Task<JoplinPagedResult<JoplinNote>> ListNotesAsync(int page, int limit, CancellationToken ct = default)
    {
        // Joplin Server exposes the item list at /api/items; we filter by suffix .md
        // and type. The Server API returns paginated results via cursor.
        var items = await ListItemsPageAsync(page, limit, ct);
        var notes = new List<JoplinNote>();
        foreach (var name in items.Names)
        {
            if (!name.EndsWith(".md", StringComparison.Ordinal)) continue;
            try
            {
                var text = await GetItemContentAsync(name, ct);
                if (text is null) continue;
                if (JoplinItemSerializer.DetectType(text) != JoplinItemSerializer.TypeNote) continue;
                notes.Add(JoplinItemSerializer.DeserializeNote(text));
            }
            catch { /* skip malformed / encrypted items */ }
        }
        return new JoplinPagedResult<JoplinNote> { Items = [.. notes], HasMore = items.HasMore };
    }

    public async Task<JoplinPagedResult<JoplinNote>> SearchNotesAsync(string query, int page, int limit, CancellationToken ct = default)
    {
        // Joplin Server does not expose full-text search via its REST API — that lives in the
        // client. We fall back to listing and filtering in-memory on title/body.
        // For production use, consider adding a separate search index.
        await EnsureAuthAsync(ct);
        var result = await ListNotesAsync(page, limit, ct);
        var lq = query.ToLowerInvariant();
        var matches = result.Items
            .Where(n => n.Title.Contains(lq, StringComparison.OrdinalIgnoreCase)
                     || n.Body.Contains(lq,  StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return new JoplinPagedResult<JoplinNote> { Items = matches, HasMore = result.HasMore };
    }

    public async Task<JoplinNote> CreateNoteAsync(CreateNoteRequest req, CancellationToken ct = default)
    {
        var id   = NewId();
        var now  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var note = new JoplinNote
        {
            Id          = id,
            Title       = req.Title,
            Body        = req.Body ?? string.Empty,
            ParentId    = req.ParentId ?? string.Empty,
            SourceUrl   = req.SourceUrl,
            IsTodo      = req.IsTodo,
            CreatedTime = now,
            UpdatedTime = now,
        };

        // If body_html supplied, store as body (Joplin Server doesn't convert HTML→MD)
        if (!string.IsNullOrEmpty(req.BodyHtml) && string.IsNullOrEmpty(req.Body))
            note = note with { Body = req.BodyHtml };

        await PutItemContentAsync($"{id}.md", JoplinItemSerializer.SerializeNote(note), ct);
        return note;
    }

    public async Task<JoplinNote> UpdateNoteAsync(string id, UpdateNoteRequest req, CancellationToken ct = default)
    {
        var existing = await GetNoteAsync(id, ct)
            ?? throw new InvalidOperationException($"Note {id} not found.");

        var updated = existing with
        {
            Title       = req.Title    ?? existing.Title,
            Body        = req.Body     ?? existing.Body,
            ParentId    = req.ParentId ?? existing.ParentId,
            UpdatedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        await PutItemContentAsync($"{id}.md", JoplinItemSerializer.SerializeNote(updated), ct);
        return updated;
    }

    public async Task DeleteNoteAsync(string id, CancellationToken ct = default)
        => await DeleteItemAsync($"{id}.md", ct);

    // ── Notebooks ─────────────────────────────────────────────────────────────

    public async Task<JoplinPagedResult<JoplinNotebook>> ListNotebooksAsync(int page, CancellationToken ct = default)
    {
        var items = await ListItemsPageAsync(page, 50, ct);
        var notebooks = new List<JoplinNotebook>();
        foreach (var name in items.Names)
        {
            if (!name.EndsWith(".md", StringComparison.Ordinal)) continue;
            try
            {
                var text = await GetItemContentAsync(name, ct);
                if (text is null) continue;
                if (JoplinItemSerializer.DetectType(text) != JoplinItemSerializer.TypeNotebook) continue;
                notebooks.Add(JoplinItemSerializer.DeserializeNotebook(text));
            }
            catch { /* skip */ }
        }
        return new JoplinPagedResult<JoplinNotebook> { Items = [.. notebooks], HasMore = items.HasMore };
    }

    public async Task<JoplinNotebook> CreateNotebookAsync(CreateNotebookRequest req, CancellationToken ct = default)
    {
        var id  = NewId();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nb  = new JoplinNotebook
        {
            Id          = id,
            Title       = req.Title,
            ParentId    = req.ParentId ?? string.Empty,
            CreatedTime = now,
            UpdatedTime = now,
        };
        await PutItemContentAsync($"{id}.md", JoplinItemSerializer.SerializeNotebook(nb), ct);
        return nb;
    }

    public async Task DeleteNotebookAsync(string id, CancellationToken ct = default)
        => await DeleteItemAsync($"{id}.md", ct);

    // ── Tags ──────────────────────────────────────────────────────────────────

    public Task<JoplinTag[]> GetNoteTagsAsync(string noteId, CancellationToken ct = default)
        // Tag relationships require scanning NoteTag items (type_: 6). For brevity,
        // return empty here — extend if tag support is needed via the Server backend.
        => Task.FromResult(Array.Empty<JoplinTag>());

    public async Task<JoplinTag> CreateTagAsync(string title, CancellationToken ct = default)
    {
        var id  = NewId();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var text = $"{title}\n\nid: {id}\ncreated_time: {JoplinItemSerializer.FormatTimestamp(now)}\nupdated_time: {JoplinItemSerializer.FormatTimestamp(now)}\ntype_: {JoplinItemSerializer.TypeTag}";
        await PutItemContentAsync($"{id}.md", text, ct);
        return new JoplinTag { Id = id, Title = title };
    }

    public async Task AddTagToNoteAsync(string tagId, string noteId, CancellationToken ct = default)
    {
        var id  = NewId();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var text = $"\n\nid: {id}\nnote_id: {noteId}\ntag_id: {tagId}\ncreated_time: {JoplinItemSerializer.FormatTimestamp(now)}\nupdated_time: {JoplinItemSerializer.FormatTimestamp(now)}\ntype_: {JoplinItemSerializer.TypeNoteTag}";
        await PutItemContentAsync($"{id}.md", text, ct);
    }

    // ── Server item API ───────────────────────────────────────────────────────

    private async Task<string?> GetItemContentAsync(string name, CancellationToken ct)
    {
        await EnsureAuthAsync(ct);
        var resp = await _http.GetAsync($"api/items/root:/{name}:/content", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    private async Task PutItemContentAsync(string name, string content, CancellationToken ct)
    {
        await EnsureAuthAsync(ct);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(content, Encoding.UTF8, "text/plain"), "file", name);
        var resp = await _http.PutAsync($"api/items/root:/{name}:/content", form, ct);
        resp.EnsureSuccessStatusCode();
    }

    private async Task DeleteItemAsync(string name, CancellationToken ct)
    {
        await EnsureAuthAsync(ct);
        var resp = await _http.DeleteAsync($"api/items/root:/{name}:", ct);
        if (resp.StatusCode != System.Net.HttpStatusCode.NotFound)
            resp.EnsureSuccessStatusCode();
    }

    private async Task<(string[] Names, bool HasMore)> ListItemsPageAsync(int page, int limit, CancellationToken ct)
    {
        await EnsureAuthAsync(ct);
        var resp = await _http.GetAsync($"api/items/root:/?limit={limit}&page={page}", ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<ServerItemListResponse>(JsonOpts, ct);
        var names = json?.Items?.Select(i => i.Name ?? string.Empty).ToArray() ?? [];
        return (names, json?.HasMore ?? false);
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    private async Task EnsureAuthAsync(CancellationToken ct)
    {
        if (_sessionToken is not null) return;

        await _authLock.WaitAsync(ct);
        try
        {
            if (_sessionToken is not null) return;

            var resp = await _http.PostAsJsonAsync("api/sessions",
                new { email = _opts.Email, password = _opts.Password }, JsonOpts, ct);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadFromJsonAsync<SessionResponse>(JsonOpts, ct);
            _sessionToken = body?.Id ?? throw new InvalidOperationException("Joplin Server returned no session id.");

            _http.DefaultRequestHeaders.Remove("X-Api-Auth");
            _http.DefaultRequestHeaders.Add("X-Api-Auth", _sessionToken);
        }
        finally
        {
            _authLock.Release();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string NewId() =>
        Guid.NewGuid().ToString("N");

    public ValueTask DisposeAsync()
    {
        _authLock.Dispose();
        return ValueTask.CompletedTask;
    }

    // ── Response DTOs (server-side only) ─────────────────────────────────────

    private sealed class SessionResponse
    {
        public string? Id      { get; init; }
        public string? UserId  { get; init; }
    }

    private sealed class ServerItemListResponse
    {
        public ServerItem[]? Items   { get; init; }

        [JsonPropertyName("has_more")]
        public bool HasMore { get; init; }
    }

    private sealed class ServerItem
    {
        public string? Name { get; init; }
        public string? Id   { get; init; }
    }
}
