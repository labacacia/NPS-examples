// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

// Interop server — a single loopback NWP Memory Node serving three fixed
// product rows. The runner script posts /query from four language clients
// and diffs the responses to verify wire-format equivalence.

using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NPS.NWP.Extensions;
using NPS.NWP.Frames;
using NPS.NWP.MemoryNode;

const string Url    = "http://127.0.0.1:17491";
const string NodeId = "urn:nps:node:demo.local:products";

var schema = new MemoryNodeSchema
{
    TableName  = "products",
    PrimaryKey = "id",
    Fields =
    [
        new() { Name = "id",    Type = "number", Nullable = false },
        new() { Name = "name",  Type = "string", Nullable = false },
        new() { Name = "price", Type = "number", Nullable = false },
    ],
};

IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
[
    Row(("id", 301), ("name", "Ergonomic Keyboard"), ("price", 129.00)),
    Row(("id", 302), ("name", "Mechanical Trackball"), ("price",  79.50)),
    Row(("id", 303), ("name", "Laminar Desk Lamp"),   ("price",  49.00)),
];

var builder = WebApplication.CreateSlimBuilder();
builder.WebHost.UseUrls(Url);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddMemoryNode<InteropProvider>(o =>
{
    o.NodeId      = NodeId;
    o.DisplayName = NodeId;
    o.Schema      = schema;
    o.PathPrefix  = "/";
});
builder.Services.AddSingleton<InteropProvider>(_ => new InteropProvider(rows));

var app = builder.Build();
app.UseMemoryNode<InteropProvider>(o =>
{
    o.NodeId     = NodeId;
    o.Schema     = schema;
    o.PathPrefix = "/";
});

Console.WriteLine($"[server] listening on {Url}");
Console.WriteLine($"[server] node_id     = {NodeId}");
Console.WriteLine($"[server] /query returns {rows.Count} product rows");

await app.RunAsync();
return;

// ── helpers ─────────────────────────────────────────────────────────────────

static Dictionary<string, object?> Row(params (string, object?)[] fields)
{
    var d = new Dictionary<string, object?>(fields.Length);
    foreach (var (k, v) in fields) d[k] = v;
    return d;
}

// ── provider ────────────────────────────────────────────────────────────────

internal sealed class InteropProvider(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) : IMemoryNodeProvider
{
    public Task<MemoryNodeQueryResult> QueryAsync(
        QueryFrame f, MemoryNodeSchema s, MemoryNodeOptions o, CancellationToken ct = default)
    {
        var limit = (int)(f.Limit == 0 ? o.DefaultLimit : f.Limit);
        return Task.FromResult(new MemoryNodeQueryResult
        {
            Rows = rows.Take(Math.Min(limit, rows.Count)).ToList(),
        });
    }

    public async IAsyncEnumerable<IReadOnlyList<IReadOnlyDictionary<string, object?>>> StreamAsync(
        QueryFrame f, MemoryNodeSchema s, MemoryNodeOptions o,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        yield return rows;
    }

    public Task<long> CountAsync(QueryFrame f, MemoryNodeSchema s, CancellationToken ct = default)
        => Task.FromResult((long)rows.Count);
}
