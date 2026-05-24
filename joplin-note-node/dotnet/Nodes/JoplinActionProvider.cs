// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using NPS.Demo.JoplinNoteNode.Client;
using NPS.NWP.ActionNode;
using NPS.NWP.Frames;

namespace NPS.Demo.JoplinNoteNode.Nodes;

/// <summary>
/// Action Node provider exposing Joplin CRUD over NWP.
/// Backed by <see cref="IJoplinBackend"/> — works with both
/// Web Clipper and Joplin Server backends.
///
/// Actions: notes.create · notes.update · notes.delete · notes.search
///          notes.clip · folders.create · folders.delete
/// </summary>
public sealed class JoplinActionProvider(IJoplinBackend joplin) : IActionNodeProvider
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public Task<ActionExecutionResult> ExecuteAsync(
        ActionFrame frame, ActionContext context, CancellationToken ct = default)
        => frame.ActionId switch
        {
            "notes.create"   => NotesCreate(frame, ct),
            "notes.update"   => NotesUpdate(frame, ct),
            "notes.delete"   => NotesDelete(frame, ct),
            "notes.search"   => NotesSearch(frame, ct),
            "notes.clip"     => NotesClip(frame, ct),
            "folders.create" => FoldersCreate(frame, ct),
            "folders.delete" => FoldersDelete(frame, ct),
            _                => throw new InvalidOperationException($"Unknown action: {frame.ActionId}"),
        };

    // ── notes.create ──────────────────────────────────────────────────────────

    private async Task<ActionExecutionResult> NotesCreate(ActionFrame frame, CancellationToken ct)
    {
        var p    = Params<NotesCreateParams>(frame);
        var note = await joplin.CreateNoteAsync(new CreateNoteRequest
        {
            Title     = p.Title,
            Body      = p.Body ?? string.Empty,
            BodyHtml  = p.BodyHtml,
            ParentId  = p.ParentId ?? string.Empty,
            SourceUrl = p.SourceUrl,
            IsTodo    = p.IsTodo ? 1 : 0,
        }, ct);

        if (p.Tags is { Length: > 0 })
        {
            foreach (var tagTitle in p.Tags)
            {
                var tag = await joplin.CreateTagAsync(tagTitle, ct);
                await joplin.AddTagToNoteAsync(tag.Id, note.Id, ct);
            }
        }

        return Result(new { note.Id, note.Title, note.ParentId });
    }

    // ── notes.update ──────────────────────────────────────────────────────────

    private async Task<ActionExecutionResult> NotesUpdate(ActionFrame frame, CancellationToken ct)
    {
        var p    = Params<NotesUpdateParams>(frame);
        var note = await joplin.UpdateNoteAsync(p.Id, new UpdateNoteRequest
        {
            Title    = p.Title,
            Body     = p.Body,
            ParentId = p.ParentId,
        }, ct);
        return Result(new { note.Id, note.Title });
    }

    // ── notes.delete ──────────────────────────────────────────────────────────

    private async Task<ActionExecutionResult> NotesDelete(ActionFrame frame, CancellationToken ct)
    {
        var p = Params<IdParams>(frame);
        await joplin.DeleteNoteAsync(p.Id, ct);
        return Result(new { deleted = p.Id });
    }

    // ── notes.search ──────────────────────────────────────────────────────────

    private async Task<ActionExecutionResult> NotesSearch(ActionFrame frame, CancellationToken ct)
    {
        var p       = Params<NotesSearchParams>(frame);
        var results = await joplin.SearchNotesAsync(p.Query, page: 1, limit: p.Limit ?? 20, ct);
        return Result(new
        {
            items    = results.Items.Select(n => new { n.Id, n.Title, n.ParentId, n.SourceUrl }),
            has_more = results.HasMore,
        });
    }

    // ── notes.clip ────────────────────────────────────────────────────────────

    private async Task<ActionExecutionResult> NotesClip(ActionFrame frame, CancellationToken ct)
    {
        var p = Params<NotesClipParams>(frame);

        using var httpClient = new System.Net.Http.HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(15);
        httpClient.DefaultRequestHeaders.Add("User-Agent", "NPS-JoplinNoteNode/1.0");

        string bodyHtml;
        string title = p.Title ?? p.Url;
        try
        {
            bodyHtml = await httpClient.GetStringAsync(p.Url, ct);
            if (p.Title is null)
            {
                var m = System.Text.RegularExpressions.Regex.Match(
                    bodyHtml, @"<title[^>]*>([^<]+)</title>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success)
                    title = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value.Trim());
            }
        }
        catch (Exception ex)
        {
            bodyHtml = $"<p>Failed to fetch URL: {ex.Message}</p>";
        }

        var note = await joplin.CreateNoteAsync(new CreateNoteRequest
        {
            Title     = title,
            BodyHtml  = bodyHtml,
            ParentId  = p.ParentId ?? string.Empty,
            SourceUrl = p.Url,
        }, ct);

        return Result(new { note.Id, note.Title, source_url = p.Url });
    }

    // ── folders.create ────────────────────────────────────────────────────────

    private async Task<ActionExecutionResult> FoldersCreate(ActionFrame frame, CancellationToken ct)
    {
        var p  = Params<FoldersCreateParams>(frame);
        var nb = await joplin.CreateNotebookAsync(new CreateNotebookRequest
        {
            Title    = p.Title,
            ParentId = p.ParentId ?? string.Empty,
        }, ct);
        return Result(new { nb.Id, nb.Title });
    }

    // ── folders.delete ────────────────────────────────────────────────────────

    private async Task<ActionExecutionResult> FoldersDelete(ActionFrame frame, CancellationToken ct)
    {
        var p = Params<IdParams>(frame);
        await joplin.DeleteNotebookAsync(p.Id, ct);
        return Result(new { deleted = p.Id });
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    private static T Params<T>(ActionFrame frame)
    {
        if (frame.Params is null)
            throw new ArgumentException($"Action '{frame.ActionId}' requires parameters.");
        return frame.Params.Value.Deserialize<T>(JsonOpts)
               ?? throw new ArgumentException($"Failed to deserialize params for '{frame.ActionId}'.");
    }

    private static ActionExecutionResult Result<T>(T value)
    {
        var json    = JsonSerializer.Serialize(value, JsonOpts);
        var element = JsonDocument.Parse(json).RootElement;
        return new ActionExecutionResult
        {
            Result   = element,
            TokenEst = (uint)Math.Ceiling(json.Length / 4.0),
        };
    }

    // ── Parameter DTOs ────────────────────────────────────────────────────────

    private sealed class NotesCreateParams
    {
        public required string Title    { get; init; }
        public string?         Body     { get; init; }
        public string?         BodyHtml { get; init; }
        [JsonPropertyName("parent_id")]  public string?  ParentId  { get; init; }
        [JsonPropertyName("source_url")] public string?  SourceUrl { get; init; }
        [JsonPropertyName("is_todo")]    public bool     IsTodo    { get; init; }
        public string[]?       Tags     { get; init; }
    }

    private sealed class NotesUpdateParams
    {
        public required string Id    { get; init; }
        public string?         Title { get; init; }
        public string?         Body  { get; init; }
        [JsonPropertyName("parent_id")] public string? ParentId { get; init; }
    }

    private sealed class NotesSearchParams
    {
        public required string Query { get; init; }
        public int?            Limit { get; init; }
    }

    private sealed class NotesClipParams
    {
        public required string Url     { get; init; }
        public string?         Title   { get; init; }
        [JsonPropertyName("parent_id")] public string? ParentId { get; init; }
    }

    private sealed class FoldersCreateParams
    {
        public required string Title    { get; init; }
        [JsonPropertyName("parent_id")] public string? ParentId { get; init; }
    }

    private sealed class IdParams
    {
        public required string Id { get; init; }
    }
}
