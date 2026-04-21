// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

// Bridge playground — one NWP Action Node exposed simultaneously as
// MCP, A2A, and gRPC. A single .NET client calls the same logical action
// through all three bridges and prints the responses side-by-side.

using System.Net.Http.Json;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using NPS.Demo.BridgePlayground;
using NPS.Demo.BridgePlayground.Grpc; // generated gRPC client (GrpcServices="Client")

// Allow plain-HTTP h2c for our loopback gRPC bridge. No production bridge
// should ever run without TLS — this knob is demo-only.
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

const string UpstreamName = "greetings";

const string UpstreamUrl = "http://127.0.0.1:17481"; // NWP Action Node
const string McpUrl      = "http://127.0.0.1:17482"; // MCP bridge
const string A2aUrl      = "http://127.0.0.1:17483"; // A2A bridge
const string GrpcUrl     = "http://127.0.0.1:17484"; // gRPC bridge

Banner("Bridge playground — MCP + A2A + gRPC fronting one NWP Action Node");

var hosts = new List<WebApplication>
{
    HostBuilders.BuildUpstream(UpstreamUrl, nodeId: "urn:nps:node:demo.local:greetings"),
    HostBuilders.BuildMcp     (McpUrl,      UpstreamName, UpstreamUrl),
    HostBuilders.BuildA2a     (A2aUrl,      UpstreamUrl),
    HostBuilders.BuildGrpc    (GrpcUrl,     UpstreamName, UpstreamUrl),
};

foreach (var h in hosts) _ = h.StartAsync();
await Task.Delay(250); // short settle — demo only

Console.WriteLine($"  upstream ▶ {UpstreamUrl}/invoke       (NWP Action Node)");
Console.WriteLine($"  MCP      ▶ {McpUrl}/mcp               (JSON-RPC tools/call)");
Console.WriteLine($"  A2A      ▶ {A2aUrl}/a2a               (JSON-RPC tasks/send)");
Console.WriteLine($"  gRPC     ▶ {GrpcUrl} /labacacia.grpc_bridge.v1.NwpBridge/Invoke");

try
{
    await ViaMcp ("A. Through MCP   (tools/call)",   McpUrl,  UpstreamName);
    await ViaA2a ("B. Through A2A   (tasks/send)",   A2aUrl);
    await ViaGrpc("C. Through gRPC  (Invoke RPC)",   GrpcUrl, UpstreamName);
}
finally
{
    foreach (var h in hosts) await h.StopAsync(TimeSpan.FromSeconds(2));
}

Console.WriteLine();
Banner("Demo complete.");
return;

// ── Scenes ─────────────────────────────────────────────────────────────────

static async Task ViaMcp(string title, string bridgeBaseUrl, string upstreamName)
{
    Console.WriteLine();
    Console.WriteLine("═══ " + title);

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

    // 1) initialize — MCP protocol requires this before tools/*
    var init = new
    {
        jsonrpc = "2.0",
        id      = 1,
        method  = "initialize",
        @params = new { protocolVersion = "2024-11-05" },
    };
    await PostJson(http, bridgeBaseUrl + "/mcp", init);

    // 2) tools/call — the bridge names the tool "{upstream}__{action_with_dots_as_underscores}"
    var toolName = $"{upstreamName}__greetings_hello";
    var call = new
    {
        jsonrpc = "2.0",
        id      = 2,
        method  = "tools/call",
        @params = new
        {
            name      = toolName,
            arguments = new { name = "Ada", via = "MCP" },
        },
    };

    var resp = await PostJson(http, bridgeBaseUrl + "/mcp", call);
    PrintBody(resp);
}

static async Task ViaA2a(string title, string bridgeBaseUrl)
{
    Console.WriteLine();
    Console.WriteLine("═══ " + title);

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

    var send = new
    {
        jsonrpc = "2.0",
        id      = 1,
        method  = "tasks/send",
        @params = new
        {
            id      = Guid.NewGuid().ToString("D"),
            message = new
            {
                role  = "user",
                parts = new object[]
                {
                    new { type = "data", data = new
                    {
                        skillId = "greetings.hello",
                        name    = "Ada",
                        via     = "A2A",
                    } },
                },
            },
        },
    };

    var resp = await PostJson(http, bridgeBaseUrl + "/a2a", send);
    PrintBody(resp);
}

static async Task ViaGrpc(string title, string bridgeBaseUrl, string upstreamName)
{
    Console.WriteLine();
    Console.WriteLine("═══ " + title);

    using var channel = GrpcChannel.ForAddress(bridgeBaseUrl);
    var client = new NwpBridge.NwpBridgeClient(channel);

    var req = new InvokeRequest
    {
        Ctx        = new UpstreamContext { Upstream = upstreamName },
        ActionId   = "greetings.hello",
        ParamsJson = ByteString.CopyFromUtf8("""{"name":"Ada","via":"gRPC"}"""),
    };

    var resp = await client.InvokeAsync(req);

    Console.WriteLine($"    → gRPC OK   upstream HTTP {resp.HttpStatus}");
    foreach (var line in Indent(resp.BodyJson.ToStringUtf8()).Split('\n'))
        Console.WriteLine("    " + line);
}

// ── helpers ────────────────────────────────────────────────────────────────

static async Task<string> PostJson(HttpClient http, string url, object body)
{
    var req = new HttpRequestMessage(HttpMethod.Post, url)
    {
        Content = JsonContent.Create(body),
    };
    var resp = await http.SendAsync(req);
    var text = await resp.Content.ReadAsStringAsync();
    Console.WriteLine($"    POST {url}");
    Console.WriteLine($"    → HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
    return text;
}

static void PrintBody(string body)
{
    foreach (var line in Indent(body).Split('\n'))
        Console.WriteLine("    " + line);
}

static string Indent(string json)
{
    // Same hand-rolled JSON indenter used in demos/nwp-graph-walk/Program.cs — the
    // slim-builder trims the reflection metadata JsonSerializer would need to walk
    // a JsonElement, and returns an empty "{}".
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
