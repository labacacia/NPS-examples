// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Http.Json;
using System.Text.Json;
using LabAcacia.McpIngress;
using Microsoft.Extensions.Configuration;
using NPS.Demo.JoplinNoteNode.Client;
using NPS.Demo.JoplinNoteNode.Nodes;
using NPS.NWP.ActionNode;
using NPS.NWP.ComplexNode;
using NPS.NWP.Extensions;
using NPS.NWP.MemoryNode;

namespace NPS.Demo.JoplinNoteNode;

public static class Cli
{
    // ── Shared options (all nullable — defaults resolved in ReadConfig) ─────────
    // Priority: CLI arg > appsettings.json > environment variable > hardcoded

    private static readonly Option<string?> BackendOpt = new(
        "--backend",
        "Backend type: webclipper | server  [cfg: Joplin:Backend | env: JOPLIN_BACKEND]");

    // WebClipper options
    private static readonly Option<string?> TokenOpt = new(
        "--token",
        "Joplin Web Clipper API token  [cfg: Joplin:WebClipper:Token | env: JOPLIN_TOKEN]");
    private static readonly Option<string?> ClipperUrlOpt = new(
        "--clipper-url",
        "Web Clipper server URL  [cfg: Joplin:WebClipper:BaseUrl | env: JOPLIN_CLIPPER_URL]");

    // Joplin Server options
    private static readonly Option<string?> ServerUrlOpt = new(
        "--server-url",
        "Joplin Server base URL  [cfg: Joplin:Server:BaseUrl | env: JOPLIN_SERVER_URL]");
    private static readonly Option<string?> EmailOpt = new(
        "--email",
        "Joplin Server account email  [cfg: Joplin:Server:Email | env: JOPLIN_EMAIL]");
    private static readonly Option<string?> PasswordOpt = new(
        "--password",
        "Joplin Server account password  [cfg: Joplin:Server:Password | env: JOPLIN_PASSWORD]");

    // Node options
    private static readonly Option<int?> PortOpt = new(
        "--port",
        "Port this NWP node listens on  [cfg: Joplin:Port | env: JOPLIN_PORT]");
    private static readonly Option<string?> NodeUrlOpt = new(
        "--node-url",
        "Public base URL of this node  [cfg: Joplin:NodeUrl | env: JOPLIN_NODE_URL]");
    private static readonly Option<bool?> McpOpt = new(
        "--mcp",
        "Enable MCP JSON-RPC endpoint at /mcp  [cfg: Joplin:Mcp | env: JOPLIN_MCP_DISABLED=true to disable]");

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name);
    private static int?    TryInt(string? s)  => int.TryParse(s, out var v)  ? v : null;
    private static bool?   TryBool(string? s) => bool.TryParse(s, out var v) ? v : null;

    // ── Entry point ───────────────────────────────────────────────────────────

    public static Task<int> RunAsync(string[] args)
    {
        var appCfg = BuildConfig();

        var root = new RootCommand("Joplin NWP Note Node — NPS example")
        {
            BuildStartCommand(appCfg),
            BuildPingCommand(appCfg),
        };

        // No subcommand → default to "start"
        root.SetHandler(async ctx =>
        {
            ctx.ExitCode = await BuildStartCommand(appCfg).InvokeAsync(ctx.ParseResult.Tokens
                .Select(t => t.Value).ToArray());
        });

        return root.InvokeAsync(args);
    }

    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

    // ── start ─────────────────────────────────────────────────────────────────

    private static Command BuildStartCommand(IConfiguration appCfg)
    {
        var cmd = new Command("start", "Start the Joplin NWP node (default command)")
        {
            BackendOpt, TokenOpt, ClipperUrlOpt,
            ServerUrlOpt, EmailOpt, PasswordOpt,
            PortOpt, NodeUrlOpt, McpOpt,
        };

        cmd.SetHandler(async ctx =>
        {
            var cfg = ReadConfig(ctx, appCfg);
            PrintBanner(cfg);
            await RunNodeAsync(cfg, ctx.GetCancellationToken());
        });

        return cmd;
    }

    // ── ping ──────────────────────────────────────────────────────────────────

    private static Command BuildPingCommand(IConfiguration appCfg)
    {
        var cmd = new Command("ping", "Test connectivity to the configured Joplin backend")
        {
            BackendOpt, TokenOpt, ClipperUrlOpt,
            ServerUrlOpt, EmailOpt, PasswordOpt,
        };

        cmd.SetHandler(async ctx =>
        {
            var cfg = ReadConfig(ctx, appCfg);
            ctx.ExitCode = await PingAsync(cfg, ctx.GetCancellationToken()) ? 0 : 1;
        });

        return cmd;
    }

    // ── NodeConfig ────────────────────────────────────────────────────────────

    private sealed record NodeConfig(
        string  Backend,
        string  ClipperUrl,
        string? Token,
        string  ServerUrl,
        string? Email,
        string? Password,
        int     Port,
        string  NodeBaseUrl,
        bool    McpEnabled);

    private static NodeConfig ReadConfig(InvocationContext ctx, IConfiguration appCfg)
    {
        // Priority: CLI arg > appsettings.json > environment variable > hardcoded default
        string Str(Option<string?> opt, string cfgKey, string envVar, string fallback) =>
            ctx.ParseResult.GetValueForOption(opt)
            ?? appCfg[cfgKey]
            ?? Env(envVar)
            ?? fallback;

        string? Opt(Option<string?> opt, string cfgKey, string envVar) =>
            ctx.ParseResult.GetValueForOption(opt)
            ?? appCfg[cfgKey]
            ?? Env(envVar);

        var port    = ctx.ParseResult.GetValueForOption(PortOpt)
                      ?? TryInt(appCfg["Joplin:Port"])
                      ?? TryInt(Env("JOPLIN_PORT"))
                      ?? 17480;

        var nodeUrl = ctx.ParseResult.GetValueForOption(NodeUrlOpt)
                      ?? appCfg["Joplin:NodeUrl"]
                      ?? Env("JOPLIN_NODE_URL")
                      ?? $"http://localhost:{port}";

        var mcp     = ctx.ParseResult.GetValueForOption(McpOpt)
                      ?? TryBool(appCfg["Joplin:Mcp"])
                      ?? (Env("JOPLIN_MCP_DISABLED") is "true" ? false : (bool?)null)
                      ?? true;

        return new NodeConfig(
            Backend:     Str(BackendOpt,    "Joplin:Backend",           "JOPLIN_BACKEND",     "webclipper"),
            ClipperUrl:  Str(ClipperUrlOpt, "Joplin:WebClipper:BaseUrl","JOPLIN_CLIPPER_URL", "http://localhost:41184"),
            Token:       Opt(TokenOpt,      "Joplin:WebClipper:Token",  "JOPLIN_TOKEN"),
            ServerUrl:   Str(ServerUrlOpt,  "Joplin:Server:BaseUrl",    "JOPLIN_SERVER_URL",  "http://localhost:22300"),
            Email:       Opt(EmailOpt,      "Joplin:Server:Email",      "JOPLIN_EMAIL"),
            Password:    Opt(PasswordOpt,   "Joplin:Server:Password",   "JOPLIN_PASSWORD"),
            Port:        port,
            NodeBaseUrl: nodeUrl,
            McpEnabled:  mcp);
    }

    // ── Web host ──────────────────────────────────────────────────────────────

    private static async Task RunNodeAsync(NodeConfig cfg, CancellationToken ct)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{cfg.Port}");

        RegisterBackend(builder.Services, cfg);
        builder.Services.AddSingleton<JoplinActionProvider>();

        Action<ComplexNodeOptions> configureComplex  = ComplexNodeConfig(cfg);
        Action<MemoryNodeOptions>  configureContent   = ContentNodeConfig();
        Action<MemoryNodeOptions>  configureNotebooks = NotebooksNodeConfig();

        builder.Services.AddComplexNode<JoplinComplexProvider>(configureComplex);
        builder.Services.AddMemoryNode<JoplinContentProvider>(configureContent);
        builder.Services.AddMemoryNode<JoplinNotebooksProvider>(configureNotebooks);

        if (cfg.McpEnabled)
        {
            var self = $"http://localhost:{cfg.Port}";
            builder.Services.AddMcpIngress(o =>
            {
                o.ServerName    = "joplin-note-node";
                o.ServerVersion = "2.0.0";
                o.Upstreams     =
                [
                    new NwpUpstream { Name = "notes",     BaseUrl = new Uri($"{self}/notes")     },
                    new NwpUpstream { Name = "content",   BaseUrl = new Uri($"{self}/content")   },
                    new NwpUpstream { Name = "notebooks", BaseUrl = new Uri($"{self}/notebooks") },
                ];
            });
        }

        var app = builder.Build();
        app.UseComplexNode<JoplinComplexProvider>(configureComplex);
        app.UseMemoryNode<JoplinContentProvider>(configureContent);
        app.UseMemoryNode<JoplinNotebooksProvider>(configureNotebooks);

        if (cfg.McpEnabled)
            app.MapMcpIngress("/mcp");

        app.MapGet("/", () => Results.Ok(new
        {
            node      = "joplin-note-node",
            version   = "2.0.0",
            backend   = cfg.Backend,
            endpoints = new[] { "/notes", "/content", "/notebooks", "/mcp" },
        }));

        await app.RunAsync(ct);
    }

    // ── Ping ──────────────────────────────────────────────────────────────────

    private static async Task<bool> PingAsync(NodeConfig cfg, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        try
        {
            if (cfg.Backend.Equals("server", StringComparison.OrdinalIgnoreCase))
            {
                Console.Write($"Pinging Joplin Server at {cfg.ServerUrl} … ");
                var resp = await http.PostAsJsonAsync(
                    $"{cfg.ServerUrl.TrimEnd('/')}/api/sessions",
                    new { email = cfg.Email, password = cfg.Password }, ct);

                if (resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                    var sessionId = body.TryGetProperty("id", out var id) ? id.GetString() : null;
                    Console.WriteLine($"OK (session={sessionId?[..8]}…)");
                    return true;
                }

                Console.WriteLine($"FAILED — HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
                return false;
            }
            else
            {
                var token = cfg.Token ?? string.Empty;
                Console.Write($"Pinging Joplin Web Clipper at {cfg.ClipperUrl} … ");
                var resp = await http.GetAsync($"{cfg.ClipperUrl.TrimEnd('/')}/ping?token={token}", ct);

                if (resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    Console.WriteLine($"OK ({body.Trim()})");
                    return true;
                }

                Console.WriteLine($"FAILED — HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED — {ex.Message}");
            return false;
        }
    }

    // ── DI helpers ────────────────────────────────────────────────────────────

    private static void RegisterBackend(IServiceCollection services, NodeConfig cfg)
    {
        if (cfg.Backend.Equals("server", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton(new JoplinServerOptions
            {
                BaseUrl  = cfg.ServerUrl,
                Email    = cfg.Email    ?? string.Empty,
                Password = cfg.Password ?? string.Empty,
            });
            services.AddHttpClient<JoplinServerBackend>();
            services.AddSingleton<IJoplinBackend>(sp => sp.GetRequiredService<JoplinServerBackend>());
        }
        else
        {
            services.AddSingleton(new JoplinClientOptions
            {
                BaseUrl = cfg.ClipperUrl,
                Token   = cfg.Token ?? string.Empty,
            });
            services.AddHttpClient<JoplinWebClipperBackend>((sp, client) =>
            {
                var opts = sp.GetRequiredService<JoplinClientOptions>();
                client.BaseAddress = new Uri(opts.BaseUrl);
                client.Timeout     = opts.Timeout;
            });
            services.AddSingleton<IJoplinBackend>(sp => sp.GetRequiredService<JoplinWebClipperBackend>());
        }
    }

    // ── Node option builders ──────────────────────────────────────────────────

    private static Action<ComplexNodeOptions> ComplexNodeConfig(NodeConfig cfg) => opts =>
    {
        opts.NodeId      = "urn:nps:node:joplin.local:notes";
        opts.DisplayName = "Joplin Notes";
        opts.PathPrefix  = "/notes";
        opts.Schema      = new MemoryNodeSchema
        {
            TableName  = "notes",
            PrimaryKey = "id",
            Fields     =
            [
                new MemoryNodeField { Name = "id",           Type = "string", Nullable = false, Description = "Joplin note ID" },
                new MemoryNodeField { Name = "title",        Type = "string", Nullable = false, Description = "Note title" },
                new MemoryNodeField { Name = "parent_id",    Type = "string", Description = "Parent notebook ID" },
                new MemoryNodeField { Name = "snippet",      Type = "string", Description = "First 200 characters of body" },
                new MemoryNodeField { Name = "source_url",   Type = "string", Description = "Clipped source URL" },
                new MemoryNodeField { Name = "is_todo",      Type = "number", Description = "1 if to-do" },
                new MemoryNodeField { Name = "created_time", Type = "number", Description = "Unix ms — created" },
                new MemoryNodeField { Name = "updated_time", Type = "number", Description = "Unix ms — last modified" },
            ],
        };
        opts.Actions = new Dictionary<string, ActionSpec>
        {
            ["notes.create"]   = new() { Description = "Create a note. Params: title, body?, body_html?, parent_id?, source_url?, is_todo?, tags[].", Async = false },
            ["notes.update"]   = new() { Description = "Update a note. Params: id, title?, body?, parent_id?.",                                        Async = false, Idempotent = true },
            ["notes.delete"]   = new() { Description = "Delete a note. Params: id.",                                                                   Async = false, Idempotent = true },
            ["notes.search"]   = new() { Description = "Search notes. Params: query, limit?.",                                                          Async = false, Idempotent = true },
            ["notes.clip"]     = new() { Description = "Clip a URL as a note. Params: url, title?, parent_id?.",                                        Async = true  },
            ["folders.create"] = new() { Description = "Create a notebook. Params: title, parent_id?.",                                                 Async = false },
            ["folders.delete"] = new() { Description = "Delete a notebook. Params: id.",                                                                Async = false, Idempotent = true },
        };
        opts.Graph =
        [
            new ComplexGraphRef("content",   $"{cfg.NodeBaseUrl}/content"),
            new ComplexGraphRef("notebooks", $"{cfg.NodeBaseUrl}/notebooks"),
        ];
        opts.GraphMaxDepth = 2;
    };

    private static Action<MemoryNodeOptions> ContentNodeConfig() => opts =>
    {
        opts.NodeId      = "urn:nps:node:joplin.local:content";
        opts.DisplayName = "Joplin Note Content";
        opts.PathPrefix  = "/content";
        opts.Schema      = new MemoryNodeSchema
        {
            TableName  = "note_content",
            PrimaryKey = "note_id",
            Fields     =
            [
                new MemoryNodeField { Name = "note_id",    Type = "string", Nullable = false },
                new MemoryNodeField { Name = "title",      Type = "string", Nullable = false },
                new MemoryNodeField { Name = "body",       Type = "string", Description = "Full Markdown body" },
                new MemoryNodeField { Name = "word_count", Type = "number" },
                new MemoryNodeField { Name = "source_url", Type = "string" },
            ],
        };
        opts.DefaultLimit       = 10;
        opts.MaxLimit           = 50;
        opts.DefaultTokenBudget = 16384;
    };

    private static Action<MemoryNodeOptions> NotebooksNodeConfig() => opts =>
    {
        opts.NodeId      = "urn:nps:node:joplin.local:notebooks";
        opts.DisplayName = "Joplin Notebooks";
        opts.PathPrefix  = "/notebooks";
        opts.Schema      = new MemoryNodeSchema
        {
            TableName  = "notebooks",
            PrimaryKey = "id",
            Fields     =
            [
                new MemoryNodeField { Name = "id",           Type = "string", Nullable = false },
                new MemoryNodeField { Name = "title",        Type = "string", Nullable = false },
                new MemoryNodeField { Name = "parent_id",    Type = "string" },
                new MemoryNodeField { Name = "created_time", Type = "number" },
                new MemoryNodeField { Name = "updated_time", Type = "number" },
            ],
        };
        opts.DefaultLimit = 100;
        opts.MaxLimit     = 500;
    };

    // ── Banner ────────────────────────────────────────────────────────────────

    private static void PrintBanner(NodeConfig cfg)
    {
        Console.WriteLine();
        Console.WriteLine("  Joplin NWP Note Node  v2.0.0");
        Console.WriteLine($"  Backend  : {cfg.Backend}");
        if (cfg.Backend.Equals("server", StringComparison.OrdinalIgnoreCase))
            Console.WriteLine($"  Server   : {cfg.ServerUrl}  ({cfg.Email})");
        else
            Console.WriteLine($"  Clipper  : {cfg.ClipperUrl}  token={Mask(cfg.Token)}");
        Console.WriteLine($"  Listening: http://0.0.0.0:{cfg.Port}");
        Console.WriteLine($"  Graph URL: {cfg.NodeBaseUrl}");
        Console.WriteLine($"  NWP      : /notes  /content  /notebooks");
        Console.WriteLine($"  MCP      : {(cfg.McpEnabled ? $"http://0.0.0.0:{cfg.Port}/mcp  (POST JSON-RPC)" : "disabled")}");
        Console.WriteLine();
    }

    private static string Mask(string? s) =>
        string.IsNullOrEmpty(s) ? "(none)" : s[..Math.Min(8, s.Length)] + "…";
}
