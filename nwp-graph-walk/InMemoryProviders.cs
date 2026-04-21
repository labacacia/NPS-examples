// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using NPS.NWP.ActionNode;
using NPS.NWP.ComplexNode;
using NPS.NWP.Frames;
using NPS.NWP.MemoryNode;

namespace NPS.Demo.GraphWalk;

/// <summary>
/// Minimal in-memory <see cref="IMemoryNodeProvider"/> backed by a fixed row list.
/// Enough to drive the Complex Node graph-traversal demo without a real database.
/// </summary>
public sealed class StaticMemoryNodeProvider(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    : IMemoryNodeProvider
{
    public Task<MemoryNodeQueryResult> QueryAsync(
        QueryFrame        frame,
        MemoryNodeSchema  schema,
        MemoryNodeOptions options,
        CancellationToken ct = default)
    {
        var limit = (int)(frame.Limit == 0 ? options.DefaultLimit : frame.Limit);
        var take  = Math.Min(limit, rows.Count);
        return Task.FromResult(new MemoryNodeQueryResult
        {
            Rows = rows.Take(take).ToList(),
        });
    }

    public async IAsyncEnumerable<IReadOnlyList<IReadOnlyDictionary<string, object?>>> StreamAsync(
        QueryFrame        frame,
        MemoryNodeSchema  schema,
        MemoryNodeOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        yield return rows;
    }

    public Task<long> CountAsync(
        QueryFrame frame, MemoryNodeSchema schema, CancellationToken ct = default)
        => Task.FromResult((long)rows.Count);
}

/// <summary>
/// Complex Node provider that owns its own row set (local data) and leaves graph
/// expansion to <c>ComplexNodeMiddleware</c>. Actions are intentionally unused.
/// </summary>
public sealed class StaticComplexNodeProvider(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    : IComplexNodeProvider
{
    public Task<MemoryNodeQueryResult> QueryAsync(
        QueryFrame frame, ComplexNodeOptions options, CancellationToken ct = default)
        => Task.FromResult(new MemoryNodeQueryResult { Rows = rows });

    public Task<ActionExecutionResult> ExecuteAsync(
        ActionFrame frame, ActionContext context, CancellationToken ct = default)
        => throw new InvalidOperationException("no actions on this Complex Node");
}
