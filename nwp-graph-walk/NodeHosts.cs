// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NPS.NWP.ComplexNode;
using NPS.NWP.Extensions;
using NPS.NWP.MemoryNode;

namespace NPS.Demo.GraphWalk;

/// <summary>
/// Kestrel host factories for the five-node topology used by the demo.
/// Each call returns a configured, un-started <see cref="WebApplication"/>.
/// </summary>
public static class NodeHosts
{
    public static WebApplication BuildMemoryNode(
        string url, string nodeId,
        MemoryNodeSchema schema,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls(url);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        // Register the framework pieces first, then override the default
        // activator-created singleton with a factory that injects our fixed rows.
        builder.Services.AddMemoryNode<StaticMemoryNodeProvider>(o =>
        {
            o.NodeId      = nodeId;
            o.DisplayName = nodeId;
            o.Schema      = schema;
            o.PathPrefix  = "/";
        });
        builder.Services.AddSingleton<StaticMemoryNodeProvider>(_ => new StaticMemoryNodeProvider(rows));

        var app = builder.Build();
        app.UseMemoryNode<StaticMemoryNodeProvider>(o =>
        {
            o.NodeId      = nodeId;
            o.Schema      = schema;
            o.PathPrefix  = "/";
        });
        return app;
    }

    public static WebApplication BuildComplexNode(
        string url, string nodeId,
        MemoryNodeSchema? localSchema,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> localRows,
        IReadOnlyList<ComplexGraphRef> graph,
        IReadOnlyList<string> allowedChildPrefixes,
        uint graphMaxDepth = 2,
        bool rejectPrivate = false)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls(url);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddComplexNode<StaticComplexNodeProvider>(o =>
        {
            o.NodeId                   = nodeId;
            o.DisplayName              = nodeId;
            o.Schema                   = localSchema;
            o.PathPrefix               = "/";
            o.Graph                    = graph;
            o.GraphMaxDepth            = graphMaxDepth;
            o.AllowedChildUrlPrefixes  = allowedChildPrefixes;
            o.RejectPrivateChildUrls   = rejectPrivate; // demo uses loopback
            o.AllowHttpChildUrls       = true;          // demo: bypass https-only check
        });
        // Override the default activator-created singleton with a factory that
        // injects our fixed local row list.
        builder.Services.AddSingleton<StaticComplexNodeProvider>(
            _ => new StaticComplexNodeProvider(localRows));

        var app = builder.Build();
        app.UseComplexNode<StaticComplexNodeProvider>(o =>
        {
            o.NodeId                   = nodeId;
            o.Schema                   = localSchema;
            o.PathPrefix               = "/";
            o.Graph                    = graph;
            o.GraphMaxDepth            = graphMaxDepth;
            o.AllowedChildUrlPrefixes  = allowedChildPrefixes;
            o.RejectPrivateChildUrls   = rejectPrivate;
            o.AllowHttpChildUrls       = true;
        });
        return app;
    }
}
