English | [中文版](./README.cn.md)

# NWP Graph Walk Demo

End-to-end exercise of **NWP Complex Node** graph traversal — the mechanism
by which a node can declare typed outbound references to other nodes, let
an Agent fan out one request across the graph, and still bound the blast
radius with a server-enforced depth cap and cycle detector.

```
                    ┌──────────────────┐
                    │ customers :17451 │  (Memory Node)
                    └────────▲─────────┘
                             │  graph.refs[customer]
                    ┌────────┴─────────┐
                    │   orders :17450  │  (Complex Node, max_depth=2)
                    └────────┬─────────┘
                             │  graph.refs[product]
                    ┌────────▼─────────┐
                    │  products :17452 │  (Memory Node)
                    └──────────────────┘

     ┌──────────────────┐        ┌──────────────────┐
     │  hub-a :17460    │ ◄────► │  hub-b :17461    │   (cycle pair)
     │  graph.refs[peer]│        │  graph.refs[peer]│
     └──────────────────┘        └──────────────────┘
```

---

## Principle — what NWP §11 actually does

NPS-2 (NWP) §11 defines the **graph walk** protocol. A Complex Node's NWM
manifest advertises `graph.refs[<rel>]` — each `rel` is a labelled edge to
another NWP node URL. An Agent issues one `POST /query` and sets
`X-NWP-Depth: N`. The Complex Node:

1. Answers its own query locally (depth `N`).
2. For every declared `graph.refs[<rel>]`, issues a child `POST /query`
   with `X-NWP-Depth: N-1` and appends its own NID to `X-NWP-Trace`.
3. Inlines each child's CapsFrame under `graph[<rel>].data`.

Two hard safety gates apply:

- **`graph_max_depth` (NWP §11.3).** A node's NWM may cap the max depth it
  is willing to serve. A request exceeding this is rejected *before* any
  child call with `NWP-DEPTH-EXCEEDED` (HTTP 400 / NPS-CLIENT-BAD-REQUEST).
- **Cycle detection via `X-NWP-Trace` (NWP §11.4).** Each hop appends the
  serving node's NID to `X-NWP-Trace`. If an incoming request already lists
  the current NID in the trace, the node returns `NWP-GRAPH-CYCLE`
  (HTTP 422 / NPS-CLIENT-UNPROCESSABLE) — the parent surfaces the error
  under `graph[].error` without failing its own response.

---

## Purpose — what this demo is for

If you're evaluating NPS as a foundation for Agent-native knowledge graphs,
you need to answer three practical questions before picking it up:

1. **Does one query really hit the whole graph?** A Memory Node + a Complex
   Node are enough to demonstrate the typed-ref fanout end-to-end.
2. **Can I bound blast radius?** Graph walks are trivially DoS-able
   without a depth cap and a cycle detector. This demo exercises both.
3. **What does an Agent actually see?** The response needs to be one
   deterministic JSON shape, not a multiplexed stream of fragments, for the
   Agent to cache by `anchor_ref`.

Five nodes on loopback, one Agent-style HTTP client, four scenes — all
within a single `dotnet run`.

---

## What it demonstrates

| # | Scene | Protocol mechanism exercised |
|---|-------|------------------------------|
| A | Depth = 0 | Baseline: Complex Node returns only its own rows, no `graph` key. |
| B | Depth = 1 | NWM `graph.refs` expanded once: `orders` fans out to `customers` + `products`, both child CapsFrames inlined. |
| C | Depth = 9 | `graph_max_depth` (2) enforced *before* any child call. |
| D | Depth = 2 with cycle | `X-NWP-Trace` catches `hub-a → hub-b → hub-a`, returns `NWP-GRAPH-CYCLE`, parent continues. |

---

## Results (from an actual run, 2026-04-21)

**Scene A — depth=0, no fanout.** Just the 3 order rows:

```
POST http://127.0.0.1:17450/query   X-NWP-Depth: 0
→ HTTP 200 OK
{
  "anchor_ref": "sha256:8a68d6e30de11fc182ed741c6d3579708812d22f9b04db1a8b007dcdfa8bc6f2",
  "count": 3,
  "data": [ {"id":1001, "customer_id":501, "product_id":301, …}, … ]
}
```

Note: **no `graph` key**. A Complex Node at depth 0 behaves exactly like a
Memory Node.

**Scene B — depth=1, one hop.** The same 3 rows, but now `graph.refs` was
expanded:

```
POST http://127.0.0.1:17450/query   X-NWP-Depth: 1
→ HTTP 200 OK
{
  "anchor_ref": "sha256:8a68d6e3…",
  "count": 3,
  "data": [ … ],
  "graph": [
    { "rel": "customer",
      "node": "http://127.0.0.1:17451",
      "data": { "anchor_ref": "sha256:ec903485…", "count": 2, "data": [ … ] } },
    { "rel": "product",
      "node": "http://127.0.0.1:17452",
      "data": { "anchor_ref": "sha256:a734e8fa…", "count": 3, "data": [ … ] } }
  ]
}
```

One round trip, two child calls (`customers` + `products`), two distinct
anchor refs — the Agent caches each child independently.

**Scene C — depth=9, rejected locally.** The Complex Node's NWM advertises
`graph_max_depth: 2`. No child call is made:

```
POST http://127.0.0.1:17450/query   X-NWP-Depth: 9
→ HTTP 400 Bad Request
{
  "frame_type": 254,
  "status": "NPS-CLIENT-BAD-REQUEST",
  "error": "NWP-DEPTH-EXCEEDED",
  "message": "X-NWP-Depth 9 exceeds node max_depth 2."
}
```

Frame type 254 = ErrorFrame. The check happens *before* fanout, so a
malicious or buggy Agent can't amplify load by asking for depth 100.

**Scene D — mutual reference, cycle caught.** `hub-a` refs `hub-b`,
`hub-b` refs `hub-a`. With depth=2 starting at `hub-a`:

```
POST http://127.0.0.1:17460/query   X-NWP-Depth: 2
→ HTTP 200 OK
{
  "anchor_ref": "sha256:779ec85b…",
  "count": 1, "data": [ {"id":1, "label":"hub-a local row"} ],
  "graph": [
    { "rel": "peer", "node": "http://127.0.0.1:17461",
      "data": {
        "count": 1, "data": [ {"id":2, "label":"hub-b local row"} ],
        "graph": [
          { "rel": "peer", "node": "http://127.0.0.1:17460",
            "error": {
              "code": "NWP-NODE-UNAVAILABLE",
              "message": "child 'peer' returned 422: {…,\"error\":\"NWP-GRAPH-CYCLE\",…graph cycle detected at 'urn:nps:node:demo.local:hub-a'.}"
            } }
        ]
      } }
  ]
}
```

The overall traversal **succeeds** (200 OK with both hubs' rows) and the
cycle is surfaced as a scoped error under `graph[].error`. An Agent can
keep the useful data and still see exactly where the loop closed.

---

## Run it

```bash
dotnet run --project demos/nwp-graph-walk
```

Requires .NET 10 SDK. All five nodes and the client run inside one process;
nothing binds beyond 127.0.0.1.

---

## Layout

```
demos/nwp-graph-walk/
├── Program.cs                 # 5 loopback nodes + 4 scenes (A/B/C/D)
├── NodeHosts.cs               # WebApplication factories (Memory / Complex)
├── InMemoryProviders.cs       # StaticMemoryNodeProvider + StaticComplexNodeProvider
├── DemoData.cs                # schemas + fixed rows (orders/customers/products/hubs)
└── NPS.Demo.GraphWalk.csproj  # .NET 10 Web SDK
```

---

## Demo-only configuration

The demo uses `http://` on loopback. Two `ComplexNodeOptions` are relaxed
so the traversal can actually execute:

| Option | Demo value | Production default | Why |
|---|---|---|---|
| `RejectPrivateChildUrls` | `false` | `true` | Loopback / RFC1918 is blocked in prod to prevent SSRF. |
| `AllowHttpChildUrls` | `true` | `false` | NPS-2 §13.2 mandates `https://` for child fetches. |

The `AllowedChildUrlPrefixes` allowlist is still enforced
(`["http://127.0.0.1:"]`) — the demo does *not* disable the allowlist, only
the scheme check.

---

## Limitations

- **No NIP.** `X-NWP-Agent` carries a bare URN; no certificate chain, no
  capability check, no rate limit.
- **Depth = 5 ceiling not separately demoed.** NPS-2 §11 caps overall depth
  at 5 regardless of `graph_max_depth`; this is covered by unit tests, not
  this demo.
- **In-process topology.** All five nodes share one process and one
  `HttpClientFactory`. Production Complex Nodes talk to independent nodes
  over HTTPS with real certificates.
