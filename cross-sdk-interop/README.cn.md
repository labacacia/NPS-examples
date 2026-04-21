[English Version](./README.md) | 中文版

# 跨 SDK 互操作演示

同一个 .NET NWP Memory Node 在**四种不同语言**的客户端面前返回**逐字节
一致**的响应——每个客户端只用各自标准库里的 HTTP + JSON 能力，**没有**任何
NPS SDK 的导入。`run.sh` 启动服务端，依次调用本机已安装的语言客户端，然后对
规范化后的载荷做 diff 比对。

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

            run.sh: 启动 → 分发 → 规范化 diff
```

---

## 原理 —— 互操作是 wire format 的属性，不是 SDK 的能力

NWP 的 `/query` 端点用普通 HTTP + JSON 把 NWP Memory Node 暴露出来。响应
是 NPS-2 §3.1 规定的 CapsFrame，形状与语言无关：
`{ anchor_ref, count, data[] }`。`anchor_ref` 是节点声明的 schema 的
SHA-256，所以只要 schema 没变，每个客户端、每种语言，拿到的 anchor 都相同。

这个属性就是整个测试的靶子。如果四种语言写的、用各自标准库的四个客户端都
能拿到**除客户端标签之外字节级一致**的 `{anchor_ref, count, data[]}`，那
互操作性就不是"共享了一个 SDK"的产物——它是 NWP wire format 本身的属性。
任何人都可以用任何语言写新客户端，不需要匹配库。

本演示故意使用 NWP 的 **JSON-Overlay** 形态（`/query` 上未加帧的
`application/json`）。带帧 wire（`application/x-nps-frame`，4 字节 header
+ tier 编码载荷）是另一个互操作面，由各语言 SDK 自己的单测覆盖。

---

## 作用 —— 回答"我非得用你的 SDK 吗？"

每次有新协议落地，最常见的反对声音都是"我们不会为了试一下就加一个依赖"。
要想在 gateway shim、curl 脚本、Postman 集合、内部胶水代码这些场景落地，
下面这条必须成立：

> **任何 HTTP 客户端都能跟 NWP 节点通信，不需要 SDK。**

这个演示就是反驳"你得用 .NET SDK / Python SDK / TS SDK / Go SDK 才能用
NWP"这种说法的最短证明。协议本身就是契约；SDK 只是方便层。

---

## 演示了什么

1. **语言无关的 wire format。** 四个客户端只用各自语言的标准库——没有
   `pip install nps-lib`、没有 `npm install @labacacia/nps-sdk`、没有
   `go get …/nps-go`。wire 上的 CapsFrame 还是同一个。
2. **确定性的 `anchor_ref`。** 每个客户端看到完全相同的 SHA-256 anchor。
   按 `anchor_ref` 缓存的 Agent 能跨语言命中。
3. **工具链可缺省。** `run.sh` 自动检测 `PATH` 上有哪些运行时；任何一个
   没装就标记为 *skipped*，不算失败。互操作命题只需要**任意两个**客户端
   一致即可证明，不强求四个全装。

---

## 运行结果（实际运行输出，2026-04-21）

`.NET` / `python3` / `node` 都装了，`go` 本机没装：

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

[client:python]  { "client": "python",  "count": 3, "anchor_ref": "sha256:a734e8fa…", "data": […完全相同的 3 行…] }
[client:nodejs]  { "client": "nodejs",  "count": 3, "anchor_ref": "sha256:a734e8fa…", "data": […完全相同的 3 行…] }
[client:go]      skipped — go not on PATH

── diff canonical outputs ──
  ✓ python == dotnet
  ✓ nodejs == dotnet
── result: interop verified across 3 clients ──
```

**解读。** 把各客户端刻意加的 `"client"` 标签剥掉之后，载荷是逐字节一致的
——同一个 `anchor_ref`、同样的 `count`、同样的行顺序、同样的数字表示
（`129` vs `129.0` 这个区别不小——无 MsgPack 的纯 JSON 必须保持一致）。
本机没装 Go，所以 diff 跑在现有的三种语言之间；这已经足够证明命题。
如果装了 Go，`run.sh` 自动会把 `go == dotnet` 加进 pass 列表。

---

## 运行

```bash
bash demos/cross-sdk-interop/run.sh
```

服务端 + dotnet 客户端需要 .NET 10 SDK。可选（每个客户端独立检测）：
`python3` ≥ 3.9、`node` ≥ 18、`go` ≥ 1.22。**任何客户端都不需要**
`pip install` / `npm install` / `go get`。

---

## 工具链矩阵

| 客户端 | 最低运行时     | 免安装依赖          |
|--------|----------------|---------------------|
| dotnet | .NET 10 SDK    | `HttpClient`        |
| python | Python 3.9+    | `urllib` 标准库     |
| nodejs | Node 18+       | 全局 `fetch()`      |
| go     | Go 1.22+       | `net/http` 标准库   |

`run.sh` 对 `PATH` 上不存在的运行时自动跳过。

---

## 目录结构

```
demos/cross-sdk-interop/
├── run.sh                                 # 启动 → 分发 → diff
├── server/
│   ├── NPS.Demo.InteropServer.csproj
│   └── Program.cs                         # 3 行商品 Memory Node
└── clients/
    ├── dotnet/
    │   ├── NPS.Demo.InteropClient.csproj
    │   └── Program.cs                     # HttpClient POST /query
    ├── python.py                          # urllib   POST /query
    ├── nodejs.mjs                         # fetch()  POST /query
    └── go.go                              # net/http POST /query
```

---

## 为什么不用官方 SDK（`nps-lib`、`@labacacia/nps-sdk`……）？

各语言 SDK 说的是**带帧**的 NCP wire——4 字节 header + tier 编码载荷
（默认 MsgPack）。而 .NET 参考实现里的 `MemoryNodeMiddleware` 目前在
`/query` 上接受未加帧的 JSON。这个演示刻意覆盖的是每个 SDK 在底层也都
支持的 JSON-Overlay 形态——因为第三方集成（curl、Postman、gateway shim）
最常走的就是这条路径。带帧 wire 的 round-trip 由各 SDK 自己的单测覆盖。

---

## 限制

- **仅单个 endpoint。** 只覆盖了 `/query`；`/anchor`、`/actions`、`/.nwm`、
  `/stream`、`/invoke`、异步轮询等均未验证。
- **未集成 NIP。** 客户端不带 `X-NWP-Agent`；服务端 `RequireAuth` 默认
  为 false，接受匿名调用。
- **带帧形态未验证。** 见上面关于 JSON-Overlay vs. `application/x-nps-frame`
  的说明。要证明 SDK 之间在带帧形态下的互操作，需要先把 .NET middleware
  的 content-type 处理与 Python / TS / Go SDK 保持一致——不在本演示范围
  内。
