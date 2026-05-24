// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace NPS.Demo.JoplinNoteNode.Client;

/// <summary>
/// <see cref="IJoplinBackend"/> backed by a self-hosted Joplin Server.
///
/// Joplin Server is a sync store, not a note REST API: every item is a text blob
/// at /api/items/root:/{id}.md:/content.  To make List and Search efficient we
/// bulk-load all items into memory on first use (write-through cache).  A real
/// Joplin client would also run a background delta-sync loop; for this example a
/// single warm-up on startup is sufficient.
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

    private readonly HttpClient          _http;
    private readonly JoplinServerOptions _opts;

    // ── Auth state ───────────────────────────────────────────────────────────
    private          string?         _sessionToken;
    private readonly SemaphoreSlim   _authLock  = new(1, 1);

    // ── Write-through in-memory cache ────────────────────────────────────────
    private readonly ConcurrentDictionary<string, JoplinNote>     _notes     = new();
    private readonly ConcurrentDictionary<string, JoplinNotebook> _notebooks = new();
    private          bool           _cacheReady;
    private readonly SemaphoreSlim  _cacheLock = new(1, 1);

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
        await EnsureReadyAsync(ct);
        return _notes.TryGetValue(id, out var note) ? note : null;
    }

    public async Task<JoplinPagedResult<JoplinNote>> ListNotesAsync(int page, int limit, CancellationToken ct = default)
    {
        await EnsureReadyAsync(ct);
        var all  = _notes.Values.OrderByDescending(n => n.UpdatedTime).ToArray();
        var skip = (page - 1) * limit;
        var items = all.Skip(skip).Take(limit).ToArray();
        return new JoplinPagedResult<JoplinNote>
        {
            Items   = items,
            HasMore = skip + items.Length < all.Length,
        };
    }

    public async Task<JoplinPagedResult<JoplinNote>> SearchNotesAsync(string query, int page, int limit, CancellationToken ct = default)
    {
        await EnsureReadyAsync(ct);
        var lq      = query.ToLowerInvariant();
        var matches = _notes.Values
            .Where(n => n.Title.Contains(lq, StringComparison.OrdinalIgnoreCase)
                     || n.Body.Contains(lq,  StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(n => n.UpdatedTime)
            .ToArray();
        var skip  = (page - 1) * limit;
        var items = matches.Skip(skip).Take(limit).ToArray();
        return new JoplinPagedResult<JoplinNote>
        {
            Items   = items,
            HasMore = skip + items.Length < matches.Length,
        };
    }

    public async Task<JoplinNote> CreateNoteAsync(CreateNoteRequest req, CancellationToken ct = default)
    {
        await EnsureReadyAsync(ct);
        var id  = NewId();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var body = req.Body ?? string.Empty;
        if (!string.IsNullOrEmpty(req.BodyHtml) && string.IsNullOrEmpty(req.Body))
            body = HtmlToMarkdown(req.BodyHtml);

        var note = new JoplinNote
        {
            Id          = id,
            Title       = req.Title,
            Body        = body,
            ParentId    = req.ParentId ?? string.Empty,
            SourceUrl   = req.SourceUrl,
            IsTodo      = req.IsTodo,
            CreatedTime = now,
            UpdatedTime = now,
        };
        await PutItemContentAsync($"{id}.md", JoplinItemSerializer.SerializeNote(note), ct);
        _notes[id] = note;
        return note;
    }

    public async Task<JoplinNote> UpdateNoteAsync(string id, UpdateNoteRequest req, CancellationToken ct = default)
    {
        await EnsureReadyAsync(ct);

        if (!_notes.TryGetValue(id, out var existing))
            existing = await FetchNoteFromServerAsync(id, ct)
                       ?? throw new InvalidOperationException($"Note {id} not found.");

        var updated = existing with
        {
            Title       = req.Title    ?? existing.Title,
            Body        = req.Body     ?? existing.Body,
            ParentId    = req.ParentId ?? existing.ParentId,
            UpdatedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        await PutItemContentAsync($"{id}.md", JoplinItemSerializer.SerializeNote(updated), ct);
        _notes[id] = updated;
        return updated;
    }

    public async Task DeleteNoteAsync(string id, CancellationToken ct = default)
    {
        await EnsureAuthAsync(ct);
        await DeleteItemAsync($"{id}.md", ct);
        _notes.TryRemove(id, out _);
    }

    // ── Notebooks ─────────────────────────────────────────────────────────────

    public async Task<JoplinPagedResult<JoplinNotebook>> ListNotebooksAsync(int page, CancellationToken ct = default)
    {
        await EnsureReadyAsync(ct);
        var all   = _notebooks.Values.OrderBy(nb => nb.Title).ToArray();
        var skip  = (page - 1) * 50;
        var items = all.Skip(skip).Take(50).ToArray();
        return new JoplinPagedResult<JoplinNotebook>
        {
            Items   = items,
            HasMore = skip + items.Length < all.Length,
        };
    }

    public async Task<JoplinNotebook> CreateNotebookAsync(CreateNotebookRequest req, CancellationToken ct = default)
    {
        await EnsureReadyAsync(ct);
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
        _notebooks[id] = nb;
        return nb;
    }

    public async Task DeleteNotebookAsync(string id, CancellationToken ct = default)
    {
        await EnsureAuthAsync(ct);
        await DeleteItemAsync($"{id}.md", ct);
        _notebooks.TryRemove(id, out _);
    }

    // ── Tags ──────────────────────────────────────────────────────────────────

    public Task<JoplinTag[]> GetNoteTagsAsync(string noteId, CancellationToken ct = default)
        // Tag relationships require scanning NoteTag items (type_: 6).
        // Not implemented for the Server backend — extend if needed.
        => Task.FromResult(Array.Empty<JoplinTag>());

    public async Task<JoplinTag> CreateTagAsync(string title, CancellationToken ct = default)
    {
        await EnsureAuthAsync(ct);
        var id  = NewId();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var text = $"{title}\n\nid: {id}\ncreated_time: {JoplinItemSerializer.FormatTimestamp(now)}\nupdated_time: {JoplinItemSerializer.FormatTimestamp(now)}\ntype_: {JoplinItemSerializer.TypeTag}";
        await PutItemContentAsync($"{id}.md", text, ct);
        return new JoplinTag { Id = id, Title = title };
    }

    public async Task AddTagToNoteAsync(string tagId, string noteId, CancellationToken ct = default)
    {
        await EnsureAuthAsync(ct);
        var id  = NewId();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var text = $"\n\nid: {id}\nnote_id: {noteId}\ntag_id: {tagId}\ncreated_time: {JoplinItemSerializer.FormatTimestamp(now)}\nupdated_time: {JoplinItemSerializer.FormatTimestamp(now)}\ntype_: {JoplinItemSerializer.TypeNoteTag}";
        await PutItemContentAsync($"{id}.md", text, ct);
    }

    // ── Cache ─────────────────────────────────────────────────────────────────

    private async Task EnsureReadyAsync(CancellationToken ct)
    {
        await EnsureAuthAsync(ct);
        await EnsureCacheAsync(ct);
    }

    private async Task EnsureCacheAsync(CancellationToken ct)
    {
        if (_cacheReady) return;
        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_cacheReady) return;
            await BulkLoadAsync(ct);
            _cacheReady = true;
        }
        finally { _cacheLock.Release(); }
    }

    private async Task BulkLoadAsync(CancellationToken ct)
    {
        var page    = 1;
        var hasMore = true;
        while (hasMore)
        {
            var (names, more) = await ListItemsPageAsync(page++, 100, ct);
            hasMore = more;
            foreach (var name in names)
            {
                if (!name.EndsWith(".md", StringComparison.Ordinal)) continue;
                try
                {
                    var text = await GetItemContentAsync(name, ct);
                    if (text is null) continue;
                    var id = name[..^3]; // strip .md
                    switch (JoplinItemSerializer.DetectType(text))
                    {
                        case JoplinItemSerializer.TypeNote:
                            _notes[id] = JoplinItemSerializer.DeserializeNote(text);
                            break;
                        case JoplinItemSerializer.TypeNotebook:
                            _notebooks[id] = JoplinItemSerializer.DeserializeNotebook(text);
                            break;
                    }
                }
                catch { /* skip encrypted / malformed items */ }
            }
        }
    }

    private async Task<JoplinNote?> FetchNoteFromServerAsync(string id, CancellationToken ct)
    {
        var text = await GetItemContentAsync($"{id}.md", ct);
        if (text is null) return null;
        return JoplinItemSerializer.DetectType(text) == JoplinItemSerializer.TypeNote
            ? JoplinItemSerializer.DeserializeNote(text)
            : null;
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
        var json  = await resp.Content.ReadFromJsonAsync<ServerItemListResponse>(JsonOpts, ct);
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

    // ── HTML → Markdown ───────────────────────────────────────────────────────

    private static string HtmlToMarkdown(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;

        var md = html;

        // Remove script/style blocks entirely
        md = Regex.Replace(md, @"<(script|style)[^>]*>[\s\S]*?</\1>", string.Empty, RegexOptions.IgnoreCase);

        // Headings h1–h6
        for (var h = 6; h >= 1; h--)
            md = Regex.Replace(md, $@"<h{h}[^>]*>([\s\S]*?)</h{h}>",
                m => new string('#', h) + " " + StripTags(m.Groups[1].Value) + "\n\n",
                RegexOptions.IgnoreCase);

        // Bold / strong
        md = Regex.Replace(md, @"<(strong|b)[^>]*>([\s\S]*?)</(strong|b)>",
            m => "**" + m.Groups[2].Value + "**", RegexOptions.IgnoreCase);

        // Italic / em
        md = Regex.Replace(md, @"<(em|i)[^>]*>([\s\S]*?)</(em|i)>",
            m => "_" + m.Groups[2].Value + "_", RegexOptions.IgnoreCase);

        // Inline code
        md = Regex.Replace(md, @"<code[^>]*>([\s\S]*?)</code>",
            m => "`" + m.Groups[1].Value + "`", RegexOptions.IgnoreCase);

        // Code blocks (pre > code or bare pre)
        md = Regex.Replace(md, @"<pre[^>]*>([\s\S]*?)</pre>",
            m => "\n```\n" + StripTags(m.Groups[1].Value).Trim('`') + "\n```\n",
            RegexOptions.IgnoreCase);

        // Links
        md = Regex.Replace(md, @"<a[^>]+href=""([^""]*)""[^>]*>([\s\S]*?)</a>",
            m => "[" + StripTags(m.Groups[2].Value) + "](" + m.Groups[1].Value + ")",
            RegexOptions.IgnoreCase);

        // Images
        md = Regex.Replace(md, @"<img[^>]+alt=""([^""]*)""[^>]+src=""([^""]*)""[^>]*\/?>",
            m => "![" + m.Groups[1].Value + "](" + m.Groups[2].Value + ")", RegexOptions.IgnoreCase);
        md = Regex.Replace(md, @"<img[^>]+src=""([^""]*)""[^>]*\/?>",
            m => "![](" + m.Groups[1].Value + ")", RegexOptions.IgnoreCase);

        // List items
        md = Regex.Replace(md, @"<li[^>]*>([\s\S]*?)</li>",
            m => "- " + StripTags(m.Groups[1].Value).Trim() + "\n", RegexOptions.IgnoreCase);
        md = Regex.Replace(md, @"</(ul|ol)>", "\n", RegexOptions.IgnoreCase);

        // Paragraphs and line breaks
        md = Regex.Replace(md, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        md = Regex.Replace(md, @"<p[^>]*>([\s\S]*?)</p>",
            m => m.Groups[1].Value.Trim() + "\n\n", RegexOptions.IgnoreCase);
        md = Regex.Replace(md, @"<div[^>]*>([\s\S]*?)</div>",
            m => m.Groups[1].Value.Trim() + "\n", RegexOptions.IgnoreCase);

        // Horizontal rule
        md = Regex.Replace(md, @"<hr\s*/?>", "\n---\n", RegexOptions.IgnoreCase);

        // Strip all remaining tags
        md = StripTags(md);

        // Decode HTML entities
        md = System.Net.WebUtility.HtmlDecode(md);

        // Normalize blank lines
        md = Regex.Replace(md, @"\n{3,}", "\n\n");

        return md.Trim();
    }

    private static string StripTags(string html)
        => Regex.Replace(html, @"<[^>]+>", string.Empty);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string NewId() => Guid.NewGuid().ToString("N");

    public ValueTask DisposeAsync()
    {
        _authLock.Dispose();
        _cacheLock.Dispose();
        return ValueTask.CompletedTask;
    }

    // ── Response DTOs (server-side only) ─────────────────────────────────────

    private sealed class SessionResponse
    {
        public string? Id     { get; init; }
        public string? UserId { get; init; }
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
