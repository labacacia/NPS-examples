// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0
//
// .NET interop client. Posts a QueryFrame to /query and prints the CapsFrame
// reduced to { count, anchor_ref, data } for cross-language diffing.

using System.Net.Http.Json;
using System.Text.Json;

const string Url = "http://127.0.0.1:17491/query";

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
using var resp = await http.PostAsJsonAsync(Url, new
{
    anchor_ref = (string?)null,
    filter     = new Dictionary<string, object?>(),
    limit      = 20u,
});
resp.EnsureSuccessStatusCode();

using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
var r = doc.RootElement;

var projection = new
{
    client     = "dotnet",
    count      = r.GetProperty("count").GetInt32(),
    anchor_ref = r.GetProperty("anchor_ref").GetString(),
    data       = r.GetProperty("data"),
};

Console.WriteLine(JsonSerializer.Serialize(projection,
    new JsonSerializerOptions { WriteIndented = true }));
