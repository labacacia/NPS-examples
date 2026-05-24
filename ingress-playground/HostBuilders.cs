// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

using LabAcacia.NPS.A2aIngress;
using LabAcacia.NPS.GrpcIngress;
using LabAcacia.NPS.McpIngress;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NPS.NWP.ActionNode;
using NPS.NWP.Extensions;

namespace NPS.Demo.IngressPlayground;

/// <summary>
/// Kestrel host factories for the 4-process playground:
///   1 upstream NWP Action Node + 3 bridge fronts (MCP / A2A / gRPC).
/// Each returns a configured <see cref="WebApplication"/> that still needs
/// <c>StartAsync</c> called on it.
/// </summary>
public static class HostBuilders
{
    /// <summary>
    /// The single upstream the three bridges all share. Exposes one action:
    /// <c>greetings.hello(name)</c>.
    /// </summary>
    public static WebApplication BuildUpstream(string url, string nodeId)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls(url);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        var actions = new Dictionary<string, ActionSpec>
        {
            ["greetings.hello"] = new ActionSpec
            {
                Description      = "NWP Action Node — bridge-playground upstream",
                Async            = false,
                Idempotent       = true,
                TimeoutMsDefault = 5_000,
                TimeoutMsMax     = 15_000,
            },
        };

        builder.Services.AddActionNode<GreetingsProvider>(o =>
        {
            o.NodeId           = nodeId;
            o.DisplayName      = nodeId;
            o.Actions          = actions;
            o.PathPrefix       = "/";
            o.DefaultTimeoutMs = 5_000;
            o.MaxTimeoutMs     = 15_000;
        });

        var app = builder.Build();
        app.UseActionNode<GreetingsProvider>(o =>
        {
            o.NodeId     = nodeId;
            o.Actions    = actions;
            o.PathPrefix = "/";
        });
        return app;
    }

    /// <summary>MCP bridge in front of <paramref name="upstreamUrl"/>.</summary>
    public static WebApplication BuildMcp(string bridgeUrl, string upstreamName, string upstreamUrl)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls(bridgeUrl);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddMcpIngress(o =>
        {
            o.ServerName    = "NPS.Demo.IngressPlayground.Mcp";
            o.ServerVersion = "0.1.0";
            o.Upstreams =
            [
                new LabAcacia.NPS.McpIngress.NwpUpstream
                {
                    Name    = upstreamName,
                    BaseUrl = new Uri(upstreamUrl),
                },
            ];
        });

        var app = builder.Build();
        app.MapMcpIngress("/mcp");
        return app;
    }

    /// <summary>A2A bridge in front of <paramref name="upstreamUrl"/>.</summary>
    public static WebApplication BuildA2a(string bridgeUrl, string upstreamUrl)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls(bridgeUrl);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddA2aIngress(o =>
        {
            o.AgentName        = "ingress-playground-greetings";
            o.AgentDescription = "NWP upstream exposed as an A2A agent.";
            o.AgentVersion     = "0.1.0";
            o.Upstream         = new A2aUpstream { BaseUrl = new Uri(upstreamUrl) };
        });

        var app = builder.Build();
        app.MapA2aIngress(rpcPath: "/a2a");
        return app;
    }

    /// <summary>
    /// gRPC bridge in front of <paramref name="upstreamUrl"/>. Kestrel is forced
    /// to HTTP/2 (plaintext h2c) because cleartext http/2 is what Grpc.Net.Client
    /// speaks with <c>http://</c> channels after enabling
    /// <c>System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport</c>.
    /// </summary>
    public static WebApplication BuildGrpc(string bridgeUrl, string upstreamName, string upstreamUrl)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        // Plain-HTTP h2c: Kestrel must be told explicitly (not via UseUrls), otherwise
        // a shared HTTP/1.1+HTTP/2 socket is negotiated and Grpc.Net.Client downgrades.
        var uri = new Uri(bridgeUrl);
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.ListenAnyIP(uri.Port, listen =>
            {
                listen.Protocols = HttpProtocols.Http2;
            });
        });

        builder.Services.AddGrpcIngress(o =>
        {
            o.Upstreams =
            [
                new LabAcacia.NPS.GrpcIngress.NwpUpstream
                {
                    Name    = upstreamName,
                    BaseUrl = new Uri(upstreamUrl),
                },
            ];
        });

        var app = builder.Build();
        app.MapGrpcIngress();
        return app;
    }
}
