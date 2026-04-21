English | [中文版](./README.cn.md)

# Bridge Playground Demo

The same logical NWP action — `greetings.hello(name)` — reached
simultaneously through **three different compatibility bridges**: MCP, A2A,
and gRPC. All three bridges front the same upstream Action Node and the
same business code; only the outward wire format differs. One in-process
.NET client drives all three channels side-by-side.

```
                      ┌──────────────────────────────┐
                      │  NWP Action Node  :17481     │
                      │  greetings.hello(name)       │
                      └──────────────▲───────────────┘
                                     │ POST /invoke  (ActionFrame)
         ┌───────────────────────────┼────────────────────────────┐
         │                           │                            │
┌────────┴────────┐        ┌─────────┴────────┐         ┌─────────┴────────┐
│ MCP bridge      │        │ A2A bridge       │         │ gRPC bridge      │
│   :17482/mcp    │        │   :17483/a2a     │         │   :17484 h2c     │
│ tools/call      │        │ tasks/send       │         │ Invoke RPC       │
└─────────────────┘        └──────────────────┘         └──────────────────┘
         ▲                           ▲                            ▲
         │                           │                            │
         └────────────┐     ┌────────┘                            │
                      │     │                                     │
                  ┌───┴─────┴──────────────────┐                  │
                  │   single .NET client       │──────────────────┘
                  │   (Program.cs)             │
                  └────────────────────────────┘
```

---

## Principle — what the bridges actually do

NWP is the canonical wire format for Agent-to-node traffic. But most of the
existing Agent ecosystem speaks something else: Anthropic MCP, Google A2A,
or plain gRPC. A bridge is a pure **shape-translator** that sits in front
of an unchanged NWP node and exposes it as:

| Bridge | What it exposes | How the payload is carried |
|--------|-----------------|----------------------------|
| `LabAcacia.McpBridge` | MCP 2024-11-05 server: `tools/list`, `tools/call` | CapsFrame serialized into `content[{type:"text", text:"..."}]` |
| `LabAcacia.A2aBridge` | Google A2A v0.2 server: `tasks/send`, `tasks/get` | CapsFrame inlined as `artifacts[].parts[{type:"data", data:…}]` |
| `LabAcacia.GrpcBridge` | gRPC service `NwpBridge.Invoke` (h2c for this demo) | CapsFrame serialized to JSON → passed as `bytes body_json` |

Because the bridges only rewrite the envelope, the same `ActionFrame`
reaches the upstream node every time, the same `greetings.hello` provider
runs, and the same CapsFrame (byte-identical except for a `via` tag the
provider writes) comes out. All three bridges reuse NWP's anchor refs,
error frames, and idempotency keys — none of that is reinvented per bridge.

---

## Purpose — why this demo exists

Protocol ecosystems are messy. MCP and A2A are already entrenched in the
Agent tooling world, and gRPC is the de-facto choice whenever someone says
"just give me a strong-typed RPC." If NPS is going to be adopted, it has
to answer one specific question:

> **Can I keep my existing MCP / A2A / gRPC clients, and still have the
> server be NWP?**

The answer is "yes, and you do not have to pick". This demo is the shortest
possible proof: one upstream, three simultaneous façades, one client that
walks the three in sequence, and the observable result is the same CapsFrame
each time — just rewrapped for each wire format.

---

## What it demonstrates

1. **One NWP action, three protocol façades.** The upstream Action Node
   has exactly one `IActionNodeProvider` implementation; MCP / A2A / gRPC
   each translate into `POST /invoke`.
2. **Shape-translation is strictly mechanical.** The bridges do not invent
   business logic — they rewrite envelopes and forward. The inner CapsFrame
   keeps the same `anchor_ref`, `count`, `data[]`, `token_est`.
3. **Bridges are composable.** All three run in the same process and point
   at the same upstream. In production each would typically be its own
   deployment, but the NWP side stays identical.

---

## Results (from an actual run, 2026-04-21)

**Scene A — through MCP.** `tools/call` with name `greetings__greetings_hello`:

```
POST http://127.0.0.1:17482/mcp     → HTTP 200 OK
{
  "jsonrpc": "2.0",
  "id": 2,
  "result": {
    "content": [
      { "type": "text",
        "text": "{\"frame_type\":4,\"preferred_tier\":1,\"anchor_ref\":\"nps://demo/bridge-playground/anchors/greeting/v1\",\"count\":1,\"data\":[{\"greeting\":\"Hello, Ada!\",\"via\":\"MCP\",\"upstream_node\":\"NWP Action Node — bridge-playground upstream\"}],\"token_est\":0}"
      }
    ],
    "isError": false
  }
}
```

MCP wraps the CapsFrame into a `content` text block per the MCP 2024-11-05
spec. The inner payload is still a standard NPS CapsFrame (frame_type 4).

**Scene B — through A2A.** `tasks/send` with skillId `greetings.hello`:

```
POST http://127.0.0.1:17483/a2a     → HTTP 200 OK
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "id": "22f0b799-549b-4c27-9c2f-620fd800b25a",
    "status": { "state": "completed", "timestamp": "2026-04-21T05:40:48…" },
    "artifacts": [
      { "name": "greetings.hello",
        "parts": [
          { "type": "data",
            "data": {
              "frame_type": 4,
              "anchor_ref": "nps://demo/bridge-playground/anchors/greeting/v1",
              "count": 1,
              "data": [ { "greeting": "Hello, Ada!", "via": "A2A", … } ],
              "token_est": 0
            } }
        ] }
    ]
  }
}
```

A2A wraps the CapsFrame as a typed `data` part inside `artifacts[]`. Same
`anchor_ref`, same payload — only the Task/Artifact envelope is A2A's.

**Scene C — through gRPC.** `NwpBridge.Invoke` over h2c:

```
→ gRPC OK   upstream HTTP 200
{
  "frame_type": 4,
  "anchor_ref": "nps://demo/bridge-playground/anchors/greeting/v1",
  "count": 1,
  "data": [ { "greeting": "Hello, Ada!", "via": "gRPC", … } ],
  "token_est": 0
}
```

The gRPC bridge does byte-level passthrough: `InvokeResponse.body_json`
carries the serialized CapsFrame unchanged.

**Interpretation.** The three `via` values (`MCP` / `A2A` / `gRPC`) are
the *only* material difference — the provider embeds them so you can tell
which channel any given response came through. The `anchor_ref` is
identical across all three, which means an Agent could cache the
response once and re-use it regardless of which bridge delivered it.

---

## Run it

```bash
dotnet run --project demos/bridge-playground
```

Requires .NET 10 SDK. Four Kestrel hosts (upstream + 3 bridges) and the
client all live in one process; nothing binds beyond 127.0.0.1.

---

## Layout

```
demos/bridge-playground/
├── Program.cs                      # 4 hosts + 3 client scenes (A/B/C)
├── HostBuilders.cs                 # Kestrel factories: upstream + 3 bridges
├── GreetingsProvider.cs            # IActionNodeProvider impl for greetings.hello
├── Protos/nwp_bridge_client.proto  # Local copy of the bridge proto (Client-only)
└── NPS.Demo.BridgePlayground.csproj
```

---

## Why a local `.proto` copy

`LabAcacia.GrpcBridge` ships its proto with `GrpcServices="Server"`, which
generates only the server base class. The demo needs a **client stub** and
also pulls in the bridge library for the server side; generating both
stubs from the same proto would collide on the
`LabAcacia.GrpcBridge.Generated.*` message types. The fix is a second
copy of the proto with a distinct
`csharp_namespace = "NPS.Demo.BridgePlayground.Grpc"` and
`GrpcServices="Client"`. Wire format is identical; only the .NET
namespaces differ, so the two sides interoperate transparently.

---

## Demo-only configuration

- **Plaintext h2c for gRPC.** The gRPC bridge listens on `http://` so the
  demo runs without a certificate. The
  `System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport` AppContext
  switch is set *only* in this demo's entry point. Production gRPC MUST
  use TLS.
- **No NIP.** Bridge calls carry no `X-NWP-Agent` header, no certificate
  chain, no capability check. The upstream accepts anonymous calls
  because `ActionNodeOptions.RequireAuth` defaults to `false`.

---

## Limitations

- **Single action.** The demo only exercises one action. MCP `resources/*`
  mapping (for Memory Nodes), A2A `artifacts[]` with multiple parts, and
  the gRPC `Query` RPC are not shown — see `compat/{mcp,a2a,grpc}-bridge/`
  tests for the full surface.
- **In-process hosts.** All four Kestrel hosts run in one process and
  share one default socket handler. Production bridges are independently
  deployed.
- **Synchronous only.** `greetings.hello` is `Async=false`; the 202 path
  (A2A `submitted → working → completed` polling, gRPC async) is not
  exercised here.
