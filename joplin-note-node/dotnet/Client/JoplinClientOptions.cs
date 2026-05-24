// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

namespace NPS.Demo.JoplinNoteNode.Client;

public sealed class JoplinClientOptions
{
    public const string Section = "Joplin:WebClipper";

    /// <summary>Base URL of the Joplin Web Clipper server, e.g. http://localhost:41184</summary>
    public string BaseUrl { get; set; } = "http://localhost:41184";

    /// <summary>API token shown in Joplin → Tools → Options → Web Clipper.</summary>
    public string Token { get; set; } = string.Empty;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}
