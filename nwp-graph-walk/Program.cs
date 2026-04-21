// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using NPS.Demo.GraphWalk;
using NPS.NWP.ComplexNode;

// Ports (loopback only). Kept contiguous for easy reading.
const string CustomersUrl = "http://127.0.0.1:17451";
const string ProductsUrl  = "http://127.0.0.1:17452";
const string OrdersUrl    = "http://127.0.0.1:17450";
const string HubAUrl      = "http://127.0.0.1:17460";
const string HubBUrl      = "http://127.0.0.1:17461";

Banner("NWP Graph Walk — Complex Node traversal + cycle detection");

// ── 1. Spin up five loopback nodes in one process ──────────────────────────
var hosts = new List<WebApplication>
{
    // Memory Nodes — plain data
    NodeHosts.BuildMemoryNode(CustomersUrl,
        nodeId: "urn:nps:node:demo.local:customers",
        schema: DemoData.CustomersSchema,
        rows:   DemoData.Customers),

    NodeHosts.BuildMemoryNode(ProductsUrl,
        nodeId: "urn:nps:node:demo.local:products",
        schema: DemoData.ProductsSchema,
        rows:   DemoData.Products),

    // Complex Node — orders + graph.refs → (customers, products)
    NodeHosts.BuildComplexNode(OrdersUrl,
        nodeId:      "urn:nps:node:demo.local:orders",
        localSchema: DemoData.OrdersSchema,
        localRows:   DemoData.Orders,
        graph:
        [
            new ComplexGraphRef("customer", CustomersUrl),
            new ComplexGraphRef("product",  ProductsUrl),
        ],
        allowedChildPrefixes: ["http://127.0.0.1:"],
        graphMaxDepth:        2,
        rejectPrivate:        false),

    // Cycle pair — hub-a and hub-b reference each other
    NodeHosts.BuildComplexNode(HubAUrl,
        nodeId:      "urn:nps:node:demo.local:hub-a",
        localSchema: DemoData.HubSchema,
        localRows:   DemoData.HubARows,
        graph: [ new ComplexGraphRef("peer", HubBUrl) ],
        allowedChildPrefixes: ["http://127.0.0.1:"],
        graphMaxDepth:        3),

    NodeHosts.BuildComplexNode(HubBUrl,
        nodeId:      "urn:nps:node:demo.local:hub-b",
        localSchema: DemoData.HubSchema,
        localRows:   DemoData.HubBRows,
        graph: [ new ComplexGraphRef("peer", HubAUrl) ],
        allowedChildPrefixes: ["http://127.0.0.1:"],
        graphMaxDepth:        3),
};

foreach (var app in hosts) _ = app.StartAsync();
await Task.WhenAll(hosts.Select(h => h.WaitForShutdownOrReadyAsync()));

try
{
    // ── Scene A — depth=0: local orders only ────────────────────────────
    await Scene("A. Depth=0 — orders only (no graph expansion)",
        url: OrdersUrl + "/query", depth: 0);

    // ── Scene B — depth=1: orders + customers + products fanout ─────────
    await Scene("B. Depth=1 — orders fan out to customers + products",
        url: OrdersUrl + "/query", depth: 1);

    // ── Scene C — depth over node cap: rejected as NWP-DEPTH-EXCEEDED ──
    await Scene("C. Depth=9 — rejected (exceeds node graph_max_depth=2)",
        url: OrdersUrl + "/query", depth: 9);

    // ── Scene D — cycle detection via X-NWP-Trace ─────────────────────
    await Scene("D. Depth=2 on hub-a → hub-b → hub-a — cycle caught",
        url: HubAUrl + "/query", depth: 2);
}
finally
{
    foreach (var h in hosts)
        await h.StopAsync(TimeSpan.FromSeconds(2));
}

Console.WriteLine();
Banner("Demo complete.");
return;

// ── helpers ─────────────────────────────────────────────────────────────────

static async Task Scene(string title, string url, int depth)
{
    Console.WriteLine();
    Console.WriteLine("═══ " + title);
    Console.WriteLine($"    POST {url}   X-NWP-Depth: {depth}");

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    var req = new HttpRequestMessage(HttpMethod.Post, url)
    {
        Content = JsonContent.Create(new
        {
            anchor_ref = (string?)null,
            filter     = new Dictionary<string, object?>(),
            limit      = 20u,
        }),
    };
    req.Headers.TryAddWithoutValidation("X-NWP-Depth", depth.ToString());
    req.Headers.TryAddWithoutValidation("X-NWP-Agent", "urn:nps:agent:demo:walker");

    HttpResponseMessage resp;
    try
    {
        resp = await http.SendAsync(req);
    }
    catch (HttpRequestException ex)
    {
        Console.WriteLine($"    ! transport error: {ex.Message}");
        return;
    }

    var body = await resp.Content.ReadAsStringAsync();
    Console.WriteLine($"    → HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
    foreach (var line in Indent(body).Split('\n'))
        Console.WriteLine("    " + line);
}

static string Indent(string json)
{
    // Tiny hand-rolled JSON indenter. We avoid JsonSerializer.Serialize on a
    // JsonElement/JsonNode because Kestrel's slim builder trims the reflection
    // metadata needed by that path and produces an empty "{}".
    var sb = new System.Text.StringBuilder(json.Length + 64);
    var depth = 0;
    var inString = false;
    var escape = false;
    foreach (var ch in json)
    {
        if (escape) { sb.Append(ch); escape = false; continue; }
        if (ch == '\\' && inString) { sb.Append(ch); escape = true; continue; }
        if (ch == '"') { inString = !inString; sb.Append(ch); continue; }
        if (inString) { sb.Append(ch); continue; }

        switch (ch)
        {
            case '{':
            case '[':
                sb.Append(ch);
                depth++;
                sb.Append('\n').Append(new string(' ', depth * 2));
                break;
            case '}':
            case ']':
                depth--;
                sb.Append('\n').Append(new string(' ', depth * 2)).Append(ch);
                break;
            case ',':
                sb.Append(ch);
                sb.Append('\n').Append(new string(' ', depth * 2));
                break;
            case ':':
                sb.Append(": ");
                break;
            default:
                sb.Append(ch);
                break;
        }
    }
    return sb.ToString();
}

static void Banner(string text)
{
    var bar = new string('─', Math.Max(text.Length, 64));
    Console.WriteLine();
    Console.WriteLine(bar);
    Console.WriteLine(text);
    Console.WriteLine(bar);
}

internal static class WebApplicationExt
{
    public static Task WaitForShutdownOrReadyAsync(this WebApplication app)
        => Task.Delay(150); // short settle — demo only
}
