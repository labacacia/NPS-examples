// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NPS.NWP.ActionNode;
using NPS.NWP.Frames;

namespace NPS.Demo.IngressPlayground;

/// <summary>
/// Sole provider the playground exposes. Implements one action:
/// <c>greetings.hello(name)</c> — returns <c>{ greeting, via }</c>. The
/// <c>via</c> field echoes the calling channel (MCP / A2A / gRPC) so the
/// side-by-side console output makes the path taken obvious.
/// </summary>
public sealed class GreetingsProvider : IActionNodeProvider
{
    public Task<ActionExecutionResult> ExecuteAsync(
        ActionFrame frame, ActionContext context, CancellationToken ct = default)
    {
        var name = "World";
        var via  = "direct";

        if (frame.Params is { } p)
        {
            if (p.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                name = n.GetString()!;
            if (p.TryGetProperty("via", out var v) && v.ValueKind == JsonValueKind.String)
                via = v.GetString()!;
        }

        var json = JsonSerializer.Serialize(new
        {
            greeting = $"Hello, {name}!",
            via,
            upstream_node = context.Spec.Description,
        });

        return Task.FromResult(new ActionExecutionResult
        {
            Result    = JsonDocument.Parse(json).RootElement,
            AnchorRef = "nps://demo/ingress-playground/anchors/greeting/v1",
        });
    }
}
