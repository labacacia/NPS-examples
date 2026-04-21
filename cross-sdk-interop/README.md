English | [中文版](./README.cn.md)

# Cross-SDK Interop Demo

The same .NET NWP Memory Node reached by **four different languages**
using nothing but their respective standard-library HTTP + JSON facilities
— no NPS SDK imported anywhere — proving that byte-identical CapsFrames
come back regardless of which client language is speaking. `run.sh`
starts the server, invokes every client whose runtime is on `PATH`, then
diffs the canonical payloads.

```
                   ┌────────────────────────────────┐
                   │  .NET NWP Memory Node :17491   │
                   │  POST /query → CapsFrame       │
                   └──────────────▲─────────────────┘
                                  │  application/json
      ┌────────────┬──────────────┼──────────────┬────────────┐
      │            │              │              │            │
 ┌────┴────┐  ┌────┴────┐   ┌─────┴────┐  ┌──────┴────┐
 │ dotnet  │  │ python3 │   │ node.js  │  │ go        │
 │ console │  │ stdlib  │   │ fetch()  │  │ net/http  │
 └─────────┘  └─────────┘   └──────────┘  └───────────┘

               run.sh: start, fan out, diff canonical outputs
```

---

## Principle — interop as a wire-format property, not an SDK feature

NWP's `/query` endpoint exposes a NWP Memory Node over plain HTTP + JSON.
The response is a CapsFrame — specified in NPS-2 §3.1 — whose shape is
language-independent: `{ anchor_ref, count, data[] }`. The `anchor_ref`
is a SHA-256 over the node's advertised schema, so if the schema hasn't
changed, every client, in every language, gets the same anchor.

That property is the whole test. If four stdlib clients in four languages
all receive the exact same `{anchor_ref, count, data[]}` modulo a tag we
add to identify the client, then interoperability is not an artifact of
a shared SDK — it's a property of the NWP wire format itself. Anyone can
write a new client, in any language, without a matching library.

This demo intentionally uses the **JSON-Overlay** flavor of NWP (unframed
`application/json` at `/query`). The framed wire (`application/x-nps-frame`,
4-byte header + tier-encoded payload) is a separate interop surface that
the language SDKs' own test suites cover.

---

## Purpose — answering "do I need your SDK?"

One of the most common objections when a new protocol lands is "we aren't
going to add a dependency just to try this." For NPS to be adopted in
gateway shims, curl scripts, Postman collections, and internal glue code,
this must be true:

> **Any HTTP client can talk to an NWP node. No SDK required.**

This demo is the shortest possible disproof of the claim that "you need
the .NET SDK / Python SDK / TS SDK / Go SDK to consume NWP." The protocol
is the contract; the SDKs are convenience layers.

---

## What it demonstrates

1. **Language-agnostic wire format.** All four clients use only their
   language's standard library — no `pip install nps-lib`, no
   `npm install @labacacia/nps-sdk`, no `go get …/nps-go`. Same CapsFrame
   on the wire.
2. **Deterministic `anchor_ref`.** Every client sees the exact same SHA-256
   anchor. An Agent caching by `anchor_ref` gets cache hits across languages.
3. **Graceful toolchain fallback.** `run.sh` auto-detects which runtimes
   are on `PATH`; any missing one is reported as *skipped*, not failed.
   The interop claim is proven by **any** two clients matching, not by
   requiring all four.

---

## Results (from an actual run, 2026-04-21)

With `.NET`, `python3`, and `node` installed (Go was not on PATH):

```
── cross-sdk-interop ──
[build] dotnet server + client
[start] server on http://127.0.0.1:17491
[client:dotnet]
{
  "client": "dotnet",
  "count": 3,
  "anchor_ref": "sha256:a734e8fa431d8f0ea186f0aee2297de63ae38b5b405edf5dfb3c6b199af64b7f",
  "data": [
    { "id": 301, "name": "Ergonomic Keyboard",    "price": 129 },
    { "id": 302, "name": "Mechanical Trackball",  "price": 79.5 },
    { "id": 303, "name": "Laminar Desk Lamp",     "price": 49 }
  ]
}

[client:python]  { "client": "python",  "count": 3, "anchor_ref": "sha256:a734e8fa…", "data": […identical 3 rows…] }
[client:nodejs]  { "client": "nodejs",  "count": 3, "anchor_ref": "sha256:a734e8fa…", "data": […identical 3 rows…] }
[client:go]      skipped — go not on PATH

── diff canonical outputs ──
  ✓ python == dotnet
  ✓ nodejs == dotnet
── result: interop verified across 3 clients ──
```

**Interpretation.** After stripping the intentional `"client"` tag (the
only difference between clients), the payloads are byte-identical — same
`anchor_ref`, same `count`, same row ordering, same numeric
representation (`129` vs `129.0` matters — MsgPack-free JSON needs that
consistent). The `go` runtime was absent on this host, so the diff ran
over the three languages that were present; that's enough to prove the
claim. If Go is installed the runner automatically adds `go == dotnet`
to the pass list.

---

## Run it

```bash
bash demos/cross-sdk-interop/run.sh
```

Requires .NET 10 SDK for the server + dotnet client. Optional (detected
per-client): `python3` ≥ 3.9, `node` ≥ 18, `go` ≥ 1.22. No
`pip install` / `npm install` / `go get` is required for any client.

---

## Toolchain matrix

| Client | Minimum runtime   | Install-free deps   |
|--------|-------------------|---------------------|
| dotnet | .NET 10 SDK       | uses `HttpClient`   |
| python | Python 3.9+       | `urllib` stdlib     |
| nodejs | Node 18+          | global `fetch()`    |
| go     | Go 1.22+          | `net/http` stdlib   |

`run.sh` skips any runtime not on `PATH`.

---

## Layout

```
demos/cross-sdk-interop/
├── run.sh                                 # start server, fan out, diff
├── server/
│   ├── NPS.Demo.InteropServer.csproj
│   └── Program.cs                         # 3-row products Memory Node
└── clients/
    ├── dotnet/
    │   ├── NPS.Demo.InteropClient.csproj
    │   └── Program.cs                     # HttpClient POST /query
    ├── python.py                          # urllib  POST /query
    ├── nodejs.mjs                         # fetch() POST /query
    └── go.go                              # net/http POST /query
```

---

## Why not use the official SDKs (`nps-lib`, `@labacacia/nps-sdk`, …)?

The SDKs speak the **framed** NCP wire format — 4-byte header + tier-encoded
payload (MsgPack by default). The .NET reference `MemoryNodeMiddleware`
today accepts unframed JSON at `/query`. This demo intentionally exercises
the JSON-Overlay flavor that every SDK also supports at a lower layer,
because that's the flavor most third-party integrations (curl, Postman,
gateway shims) will actually speak. For the framed wire format, each SDK's
own test suite covers codec round-trips.

---

## Limitations

- **Single endpoint.** Only `/query` is exercised. `/anchor`, `/actions`,
  `/.nwm`, `/stream`, `/invoke`, async polling — all unvalidated here.
- **No NIP.** Clients send no `X-NWP-Agent`; the server accepts anonymous
  calls (`RequireAuth` defaults to false).
- **Framed wire untested.** See note above about JSON-Overlay vs.
  `application/x-nps-frame`. Cross-SDK framed interop would require
  aligning the .NET middleware's content-type handling with what the
  Python / TS / Go SDKs emit — out of scope for this demo.
