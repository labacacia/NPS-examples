// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

using NPS.NWP.MemoryNode;

namespace NPS.Demo.GraphWalk;

/// <summary>
/// Fixed row sets and schemas used by the graph-walk demo. Three Memory Nodes
/// (orders / customers / products) plus two Complex Nodes forming a cycle
/// (<c>hub-a</c> ↔ <c>hub-b</c>) to exercise the NPS-2 §11 cycle detector.
/// </summary>
public static class DemoData
{
    // ── Schemas ──────────────────────────────────────────────────────────────

    public static readonly MemoryNodeSchema OrdersSchema = new()
    {
        TableName  = "orders",
        PrimaryKey = "id",
        Fields =
        [
            new() { Name = "id",          Type = "number",   Nullable = false },
            new() { Name = "customer_id", Type = "number",   Nullable = false },
            new() { Name = "product_id",  Type = "number",   Nullable = false },
            new() { Name = "qty",         Type = "number",   Nullable = false },
            new() { Name = "placed_at",   Type = "datetime", Nullable = false },
        ],
    };

    public static readonly MemoryNodeSchema CustomersSchema = new()
    {
        TableName  = "customers",
        PrimaryKey = "id",
        Fields =
        [
            new() { Name = "id",    Type = "number",  Nullable = false },
            new() { Name = "name",  Type = "string",  Nullable = false },
            new() { Name = "email", Type = "string",  Nullable = false },
        ],
    };

    public static readonly MemoryNodeSchema ProductsSchema = new()
    {
        TableName  = "products",
        PrimaryKey = "id",
        Fields =
        [
            new() { Name = "id",    Type = "number",  Nullable = false },
            new() { Name = "name",  Type = "string",  Nullable = false },
            new() { Name = "price", Type = "number",  Nullable = false },
        ],
    };

    // ── Row fixtures ─────────────────────────────────────────────────────────

    public static readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> Orders =
    [
        Row(("id", 1001), ("customer_id", 501), ("product_id", 301), ("qty", 2),
            ("placed_at", "2026-04-20T09:15:00Z")),
        Row(("id", 1002), ("customer_id", 502), ("product_id", 302), ("qty", 1),
            ("placed_at", "2026-04-20T11:32:00Z")),
        Row(("id", 1003), ("customer_id", 501), ("product_id", 303), ("qty", 5),
            ("placed_at", "2026-04-20T14:08:00Z")),
    ];

    public static readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> Customers =
    [
        Row(("id", 501), ("name", "Acacia Labs"),  ("email", "ops@acacia.example")),
        Row(("id", 502), ("name", "Lotus Holdings"),("email", "hello@lotus.example")),
    ];

    public static readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> Products =
    [
        Row(("id", 301), ("name", "Proto Reader"),     ("price", 49.99)),
        Row(("id", 302), ("name", "Anchor Cache Pro"), ("price", 129.00)),
        Row(("id", 303), ("name", "Frame Lens"),       ("price", 19.95)),
    ];

    public static readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> HubARows =
    [
        Row(("id", 1), ("label", "hub-a local row")),
    ];

    public static readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> HubBRows =
    [
        Row(("id", 2), ("label", "hub-b local row")),
    ];

    public static readonly MemoryNodeSchema HubSchema = new()
    {
        TableName  = "hub",
        PrimaryKey = "id",
        Fields =
        [
            new() { Name = "id",    Type = "number", Nullable = false },
            new() { Name = "label", Type = "string", Nullable = false },
        ],
    };

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, object?> Row(
        params (string key, object value)[] kv)
    {
        var d = new Dictionary<string, object?>(kv.Length);
        foreach (var (k, v) in kv) d[k] = v;
        return d;
    }
}
