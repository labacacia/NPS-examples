// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

namespace NPS.Demo.JoplinNoteNode.Client;

public sealed class JoplinServerOptions
{
    public const string Section = "Joplin:Server";

    /// <summary>Base URL of the Joplin Server, e.g. http://localhost:22300</summary>
    public string BaseUrl { get; set; } = "http://localhost:22300";

    public string Email    { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);
}
