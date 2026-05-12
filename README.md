English | [中文版](./README.cn.md)

# NPS Examples

Curated, runnable demos of the [Neural Protocol Suite (NPS)](https://github.com/labacacia/nps).
Each demo is a self-contained exercise of one protocol mechanism end-to-end:
you can read the source, read the README, and — with the NPS monorepo checked
out — run it.

> **Source of truth.** These demos also live inside the main NPS monorepo
> at `demos/`. This repo exists so the demos are findable on their own
> without cloning the whole suite. To *run* a demo, clone
> [`labacacia/nps`](https://github.com/labacacia/nps) and invoke it from
> there — the `.csproj` files reference NPS packages (`NPS.Core`, `NPS.NWP`,
> `LabAcacia.McpIngress`, `LabAcacia.A2aIngress`, `LabAcacia.GrpcIngress`) via
> `ProjectReference` paths that only resolve inside the monorepo until
> those packages ship on NuGet.

---

## The demos

| # | Demo | One-line description | What protocol piece it exercises |
|---|------|----------------------|----------------------------------|
| 1 | [**nwp-graph-walk**](./nwp-graph-walk/README.md) | Complex Node traversing typed refs to other NWP nodes, with depth cap + cycle detection | NPS-2 (NWP) §11 `graph.refs`, `X-NWP-Depth`, `X-NWP-Trace` |
| 2 | [**ingress-playground**](./ingress-playground/README.md) | One NWP Action Node exposed simultaneously through MCP, A2A, and gRPC ingresses | `LabAcacia.McpIngress` + `LabAcacia.A2aIngress` + `LabAcacia.GrpcIngress` |
| 3 | [**cross-sdk-interop**](./cross-sdk-interop/README.md) | Same .NET NWP Memory Node reached from dotnet / python / nodejs / go using only stdlib HTTP + JSON | NWP JSON-Overlay at `/query`, CapsFrame byte-equality across clients |

Each demo's README has four sections:

1. **Principle** — the NPS spec mechanism being exercised.
2. **Purpose** — what question the demo answers.
3. **What it demonstrates** — specific observable behaviors.
4. **Results** — captured output from an actual run, with commentary.

---

## How to run a demo

```bash
git clone https://github.com/labacacia/nps.git
cd nps

# Demo 1 — NWP graph walk (one process, 5 loopback nodes, 4 scenes)
dotnet run --project demos/nwp-graph-walk

# Demo 2 — bridge playground (upstream + 3 bridges + client in one process)
dotnet run --project demos/ingress-playground

# Demo 3 — cross-SDK interop (starts server, fans out to each runtime on PATH)
bash demos/cross-sdk-interop/run.sh
```

Requires .NET 10 SDK. Demo 3 additionally detects `python3`, `node`, and
`go` on `PATH`; missing runtimes are skipped gracefully.

---

## Why a separate repo

Three reasons, in order of importance:

1. **Discoverability.** Someone searching "NPS MCP ingress example" or
   "NWP graph walk demo" finds a self-describing repo, not a subdirectory
   three levels deep in a monorepo.
2. **Smaller clone surface.** The main monorepo contains the full spec,
   six SDKs, three bridges, and six NIP CA implementations. You don't want
   to clone all that just to read a 150-line Program.cs.
3. **Stable showcase.** Monorepo HEAD moves constantly — protocol
   versions bump, tests get added, drafts land. A separate examples repo
   can be tagged independently to match specific NPS release milestones.

---

## License

Apache-2.0. See [`LICENSE`](./LICENSE) and [`NOTICE`](./NOTICE).

## Issues & contributions

- Open issues against this repo for problems *with the demos themselves*
  (typos, unclear explanation, outdated output).
- Open issues against [`labacacia/nps`](https://github.com/labacacia/nps/issues)
  for protocol bugs or spec discussion — the source of those bugs is
  upstream, not here.
- See [`CONTRIBUTING.md`](./CONTRIBUTING.md) for the branch / PR workflow.
