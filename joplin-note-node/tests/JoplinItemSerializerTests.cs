// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

using NPS.Demo.JoplinNoteNode;
using NPS.Demo.JoplinNoteNode.Client;
using Xunit;

namespace NPS.Demo.JoplinNoteNode.Tests;

public class JoplinItemSerializerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static JoplinNote SampleNote(
        string title   = "Test Note",
        string body    = "Body content",
        string id      = "aaaabbbbccccdddd0000111122223333",
        string parentId = "ffffeeeeddddcccc0000111122223333",
        long created   = 1_716_278_400_000L,
        long updated   = 1_716_278_500_000L) => new()
    {
        Id          = id,
        Title       = title,
        Body        = body,
        ParentId    = parentId,
        CreatedTime = created,
        UpdatedTime = updated,
        IsTodo      = 0,
        SourceUrl   = null,
    };

    private static JoplinNotebook SampleNotebook(
        string title = "My Notebook",
        string id    = "nb000000000000000000000000000001") => new()
    {
        Id          = id,
        Title       = title,
        ParentId    = string.Empty,
        CreatedTime = 1_716_278_400_000L,
        UpdatedTime = 1_716_278_500_000L,
    };

    // ── Round-trip: Note ──────────────────────────────────────────────────────

    [Fact]
    public void Note_RoundTrip_PreservesAllFields()
    {
        var note = SampleNote();
        var text = JoplinItemSerializer.SerializeNote(note);
        var back = JoplinItemSerializer.DeserializeNote(text);

        Assert.Equal(note.Id,          back.Id);
        Assert.Equal(note.Title,       back.Title);
        Assert.Equal(note.Body,        back.Body);
        Assert.Equal(note.ParentId,    back.ParentId);
        Assert.Equal(note.CreatedTime, back.CreatedTime);
        Assert.Equal(note.UpdatedTime, back.UpdatedTime);
        Assert.Equal(note.IsTodo,      back.IsTodo);
    }

    [Fact]
    public void Notebook_RoundTrip_PreservesAllFields()
    {
        var nb   = SampleNotebook();
        var text = JoplinItemSerializer.SerializeNotebook(nb);
        var back = JoplinItemSerializer.DeserializeNotebook(text);

        Assert.Equal(nb.Id,          back.Id);
        Assert.Equal(nb.Title,       back.Title);
        Assert.Equal(nb.ParentId,    back.ParentId);
        Assert.Equal(nb.CreatedTime, back.CreatedTime);
        Assert.Equal(nb.UpdatedTime, back.UpdatedTime);
    }

    // ── Title edge cases ──────────────────────────────────────────────────────

    [Fact]
    public void Note_TitleWithSpecialCharacters_PreservedExactly()
    {
        var note = SampleNote(title: "Hello: World — «quoted» 你好");
        var back = JoplinItemSerializer.DeserializeNote(JoplinItemSerializer.SerializeNote(note));
        Assert.Equal(note.Title, back.Title);
    }

    [Fact]
    public void Note_EmptyTitle_RoundTrips()
    {
        var note = SampleNote(title: string.Empty);
        var back = JoplinItemSerializer.DeserializeNote(JoplinItemSerializer.SerializeNote(note));
        Assert.Equal(string.Empty, back.Title);
    }

    [Fact]
    public void Notebook_TitleWithUnicode_Preserved()
    {
        var nb   = SampleNotebook(title: "📓 My Notes — 笔记本");
        var back = JoplinItemSerializer.DeserializeNotebook(JoplinItemSerializer.SerializeNotebook(nb));
        Assert.Equal(nb.Title, back.Title);
    }

    // ── Body edge cases ───────────────────────────────────────────────────────

    [Fact]
    public void Note_EmptyBody_RoundTrips()
    {
        var note = SampleNote(body: string.Empty);
        var back = JoplinItemSerializer.DeserializeNote(JoplinItemSerializer.SerializeNote(note));
        Assert.Equal(string.Empty, back.Body);
    }

    [Fact]
    public void Note_BodyWithInternalBlankLines_Preserved()
    {
        const string body = "First paragraph.\n\nSecond paragraph.\n\nThird.";
        var note = SampleNote(body: body);
        var back = JoplinItemSerializer.DeserializeNote(JoplinItemSerializer.SerializeNote(note));
        Assert.Equal(body, back.Body);
    }

    [Fact]
    public void Note_BodyWithPropLikeLines_NotConfusedWithProps()
    {
        const string body = "key: value\nanother: thing\ntype_: 99";
        var note = SampleNote(body: body);
        var back = JoplinItemSerializer.DeserializeNote(JoplinItemSerializer.SerializeNote(note));
        Assert.Equal(note.Title, back.Title);
        Assert.Equal(body,       back.Body);
    }

    [Fact]
    public void Note_LongBody_RoundTrips()
    {
        var body = string.Join("\n", Enumerable.Range(1, 500).Select(i => $"Line {i}: " + new string('x', 80)));
        var note = SampleNote(body: body);
        var back = JoplinItemSerializer.DeserializeNote(JoplinItemSerializer.SerializeNote(note));
        Assert.Equal(body, back.Body);
    }

    // ── Prop value escaping ───────────────────────────────────────────────────

    [Fact]
    public void PropEscaping_NewlineInSourceUrl_EscapedAndRestored()
    {
        var note = SampleNote() with { SourceUrl = "https://example.com/a\nb" };
        var text = JoplinItemSerializer.SerializeNote(note);

        // The escaped value must appear literally as \n (backslash+n) in the serialized text,
        // not as a real newline that would break the prop-value line.
        Assert.Contains("source_url: https://example.com/a\\nb", text);

        var back = JoplinItemSerializer.DeserializeNote(text);
        Assert.Equal("https://example.com/a\nb", back.SourceUrl);
    }

    [Fact]
    public void PropEscaping_LiteralBackslashN_DistinguishedFromNewline()
    {
        // A literal \n (backslash + n) in a prop value must survive round-trip
        // without being confused with a real newline character.
        var note = SampleNote() with { SourceUrl = "https://example.com/path\\nmore" };
        var back = JoplinItemSerializer.DeserializeNote(JoplinItemSerializer.SerializeNote(note));
        Assert.Equal("https://example.com/path\\nmore", back.SourceUrl);
    }

    [Fact]
    public void PropEscaping_CarriageReturn_EscapedAndRestored()
    {
        var note = SampleNote() with { SourceUrl = "url\rwith\rCR" };
        var back = JoplinItemSerializer.DeserializeNote(JoplinItemSerializer.SerializeNote(note));
        Assert.Equal("url\rwith\rCR", back.SourceUrl);
    }

    // ── Timestamp handling ────────────────────────────────────────────────────

    [Fact]
    public void Timestamp_ZeroValue_RoundTripsAsZero()
    {
        var note = SampleNote(created: 0, updated: 0);
        var back = JoplinItemSerializer.DeserializeNote(JoplinItemSerializer.SerializeNote(note));
        Assert.Equal(0L, back.CreatedTime);
        Assert.Equal(0L, back.UpdatedTime);
    }

    [Fact]
    public void Timestamp_MillisecondPrecision_PreservedWithinOneSecond()
    {
        long ms = 1_716_278_400_123L;
        var note = SampleNote(created: ms);
        var back = JoplinItemSerializer.DeserializeNote(JoplinItemSerializer.SerializeNote(note));
        // Allow ±500ms tolerance for format rounding
        Assert.InRange(back.CreatedTime, ms - 500, ms + 500);
    }

    [Theory]
    [InlineData("2026-05-21T10:00:00.000Z", 1_779_357_600_000L)]
    [InlineData("2026-01-01T00:00:00.000Z", 1_767_225_600_000L)]
    public void Timestamp_ParseISO8601_CorrectUnixMs(string iso, long expectedMs)
    {
        long parsed = JoplinItemSerializer.ParseTimestamp(iso);
        Assert.InRange(parsed, expectedMs - 1000, expectedMs + 1000);
    }

    [Fact]
    public void Timestamp_FormatAndParse_RoundTrip()
    {
        long ms = 1_716_278_400_000L;
        string formatted = JoplinItemSerializer.FormatTimestamp(ms);
        long back = JoplinItemSerializer.ParseTimestamp(formatted);
        Assert.Equal(ms, back);
    }

    // ── Type detection ────────────────────────────────────────────────────────

    [Fact]
    public void DetectType_NoteText_Returns1()
    {
        var text = JoplinItemSerializer.SerializeNote(SampleNote());
        Assert.Equal(JoplinItemSerializer.TypeNote, JoplinItemSerializer.DetectType(text));
    }

    [Fact]
    public void DetectType_NotebookText_Returns2()
    {
        var text = JoplinItemSerializer.SerializeNotebook(SampleNotebook());
        Assert.Equal(JoplinItemSerializer.TypeNotebook, JoplinItemSerializer.DetectType(text));
    }

    [Fact]
    public void DetectType_EmptyString_ReturnsMinus1()
    {
        Assert.Equal(-1, JoplinItemSerializer.DetectType(string.Empty));
    }

    // ── Encrypted items ───────────────────────────────────────────────────────

    [Fact]
    public void DeserializeNote_EncryptedItem_ThrowsInvalidOperation()
    {
        const string encryptedItem = """


            id: aaaabbbbccccdddd0000111122223333
            parent_id: ffffeeeeddddcccc0000111122223333
            created_time: 2026-05-21T10:00:00.000Z
            updated_time: 2026-05-21T10:00:00.000Z
            encryption_applied: 1
            encryption_cipher_text: JED01000000...fakecipher...
            type_: 1
            """;

        Assert.Throws<InvalidOperationException>(() =>
            JoplinItemSerializer.DeserializeNote(encryptedItem));
    }

    // ── Wrong type ────────────────────────────────────────────────────────────

    [Fact]
    public void DeserializeNote_GivenNotebookText_ThrowsInvalidOperation()
    {
        var notebookText = JoplinItemSerializer.SerializeNotebook(SampleNotebook());
        Assert.Throws<InvalidOperationException>(() =>
            JoplinItemSerializer.DeserializeNote(notebookText));
    }

    [Fact]
    public void DeserializeNotebook_GivenNoteText_ThrowsInvalidOperation()
    {
        var noteText = JoplinItemSerializer.SerializeNote(SampleNote());
        Assert.Throws<InvalidOperationException>(() =>
            JoplinItemSerializer.DeserializeNotebook(noteText));
    }

    // ── Serialized output structure ───────────────────────────────────────────

    [Fact]
    public void Serialize_NoteOutput_ContainsTypeField()
    {
        var text = JoplinItemSerializer.SerializeNote(SampleNote());
        Assert.Contains("type_: 1", text);
    }

    [Fact]
    public void Serialize_NotebookOutput_ContainsTypeField()
    {
        var text = JoplinItemSerializer.SerializeNotebook(SampleNotebook());
        Assert.Contains("type_: 2", text);
    }

    [Fact]
    public void Serialize_Note_TitleOnFirstLine()
    {
        var note = SampleNote(title: "My Title");
        var text = JoplinItemSerializer.SerializeNote(note);
        Assert.StartsWith("My Title", text);
    }

    [Fact]
    public void Serialize_Note_IdInProps()
    {
        var note = SampleNote(id: "aaaabbbbccccdddd0000111122223333");
        var text = JoplinItemSerializer.SerializeNote(note);
        Assert.Contains("id: aaaabbbbccccdddd0000111122223333", text);
    }
}
