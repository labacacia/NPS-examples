// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace NPS.Demo.JoplinNoteNode;

public sealed record JoplinNote
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    [JsonPropertyName("parent_id")]    public string  ParentId      { get; init; } = string.Empty;
    [JsonPropertyName("created_time")] public long    CreatedTime   { get; init; }
    [JsonPropertyName("updated_time")] public long    UpdatedTime   { get; init; }
    [JsonPropertyName("is_todo")]      public int     IsTodo        { get; init; }
    [JsonPropertyName("todo_completed")] public int   TodoCompleted { get; init; }
    [JsonPropertyName("source_url")]   public string? SourceUrl     { get; init; }
}

public sealed record JoplinNotebook
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    [JsonPropertyName("parent_id")]    public string ParentId    { get; init; } = string.Empty;
    [JsonPropertyName("created_time")] public long   CreatedTime { get; init; }
    [JsonPropertyName("updated_time")] public long   UpdatedTime { get; init; }
}

public sealed class JoplinTag
{
    public string Id    { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
}

public sealed class JoplinPagedResult<T>
{
    public required T[] Items { get; init; }
    [JsonPropertyName("has_more")] public bool HasMore { get; init; }
}

public sealed class CreateNoteRequest
{
    public required string Title    { get; init; }
    public string  Body     { get; init; } = string.Empty;
    [JsonPropertyName("parent_id")]  public string  ParentId  { get; init; } = string.Empty;
    [JsonPropertyName("source_url")] public string? SourceUrl { get; init; }
    [JsonPropertyName("body_html")]  public string? BodyHtml  { get; init; }
    [JsonPropertyName("is_todo")]    public int     IsTodo    { get; init; } = 0;
}

public sealed class UpdateNoteRequest
{
    public string? Title    { get; init; }
    public string? Body     { get; init; }
    [JsonPropertyName("parent_id")]    public string? ParentId      { get; init; }
    [JsonPropertyName("is_todo")]      public int?    IsTodo        { get; init; }
    [JsonPropertyName("todo_completed")] public int?  TodoCompleted { get; init; }
}

public sealed class CreateNotebookRequest
{
    public required string Title    { get; init; }
    [JsonPropertyName("parent_id")] public string ParentId { get; init; } = string.Empty;
}
