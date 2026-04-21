[English Version](./README.md) | 中文版

# NWP Graph Walk 演示

**NWP Complex Node** 图谱遍历的端到端演示——一个节点声明若干个带标签的出边
指向其他节点，Agent 发一次请求就能扇出整张子图，同时靠服务端强制的深度上限
和环路检测把影响范围卡住。

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
     │  hub-a :17460    │ ◄────► │  hub-b :17461    │   (互相引用，成环)
     │  graph.refs[peer]│        │  graph.refs[peer]│
     └──────────────────┘        └──────────────────┘
```

---

## 原理 —— NWP §11 到底在干什么

NPS-2 (NWP) §11 定义了 **graph walk** 协议。Complex Node 的 NWM 清单里声明
`graph.refs[<rel>]`——每个 `rel` 就是一条带标签的、指向另一个 NWP 节点 URL
的出边。Agent 发一个 `POST /query` 并附上 `X-NWP-Depth: N`，节点就会：

1. 本地把自己的查询答完（对应深度 `N`）。
2. 对每个声明的 `graph.refs[<rel>]`，发起一个子请求 `POST /query`，
   `X-NWP-Depth` 递减为 `N-1`，`X-NWP-Trace` 追加自己的 NID。
3. 把每个子节点的 CapsFrame 内联挂在 `graph[<rel>].data` 下。

两道硬性安全闸：

- **`graph_max_depth`（NWP §11.3）。** 节点 NWM 里可以声明自己愿意服务的
  最大深度。超过这个值直接在本地拒绝，**一次子请求都不发**，返回
  `NWP-DEPTH-EXCEEDED`（HTTP 400 / NPS-CLIENT-BAD-REQUEST）。
- **基于 `X-NWP-Trace` 的环路检测（NWP §11.4）。** 每一跳都把承载节点的
  NID 追加到 `X-NWP-Trace`。如果进来的请求已经包含当前节点的 NID，节点就
  返回 `NWP-GRAPH-CYCLE`（HTTP 422 / NPS-CLIENT-UNPROCESSABLE），由父节点
  把错误以 `graph[].error` 的形式暴露出来——整条遍历不失败。

---

## 作用 —— 这个演示解答什么问题

如果你正在评估 NPS 能不能作为 Agent 原生知识图谱的底座，落地之前你至少得
回答三个工程上的问题：

1. **一次查询真能打穿整张图吗？** Memory Node + Complex Node 两种节点就够
   把 typed-ref 扇出的全链路走一遍。
2. **影响半径能卡住吗？** 没有深度上限和环路检测的图谱遍历天然是 DoS 载体。
   这个演示把两道闸都打一遍。
3. **Agent 看到的是什么？** 响应必须是一份确定性的 JSON，而不是流式碎片，
   Agent 才能按 `anchor_ref` 缓存。

五个节点跑在 loopback，一个 Agent 风格的 HTTP 客户端，四个场景——全部塞进
一次 `dotnet run`。

---

## 演示了什么

| # | 场景 | 被演练的协议机制 |
|---|------|------------------|
| A | Depth = 0 | 基线：Complex Node 只返回自己的行，不带 `graph` 字段。 |
| B | Depth = 1 | NWM `graph.refs` 扇出一跳：`orders` → `customers` + `products`，两个子 CapsFrame 都内联在响应里。 |
| C | Depth = 9 | `graph_max_depth`（= 2）在本地拒绝，**不发起任何子请求**。 |
| D | Depth = 2，成环 | `X-NWP-Trace` 抓到 `hub-a → hub-b → hub-a`，返回 `NWP-GRAPH-CYCLE`，父节点继续把有用数据给出。 |

---

## 运行结果（实际运行输出，2026-04-21）

**场景 A —— depth=0，不扇出。** 就三条订单：

```
POST http://127.0.0.1:17450/query   X-NWP-Depth: 0
→ HTTP 200 OK
{
  "anchor_ref": "sha256:8a68d6e30de11fc182ed741c6d3579708812d22f9b04db1a8b007dcdfa8bc6f2",
  "count": 3,
  "data": [ {"id":1001, "customer_id":501, "product_id":301, …}, … ]
}
```

注意：**没有 `graph` 字段**。Complex Node 在 depth=0 时表现和 Memory Node
完全一致。

**场景 B —— depth=1，扇出一跳。** 同样三条订单，但是 `graph.refs` 被展开
了：

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

一次外部往返，两个子调用（`customers` + `products`），两个独立的
`anchor_ref`——Agent 可以各自缓存。

**场景 C —— depth=9，本地拒绝。** Complex Node NWM 上声明
`graph_max_depth: 2`，**一次子请求都不发**：

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

`frame_type: 254` 就是 ErrorFrame。校验发生在扇出**之前**，所以有问题
（或者被利用）的 Agent 没办法通过要求 depth=100 放大负载。

**场景 D —— 互相引用，环路被抓住。** `hub-a` 引用 `hub-b`，`hub-b` 引用
`hub-a`。以 depth=2 从 `hub-a` 起步：

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

整条遍历**成功**（200，两个 hub 的行都返回了），环路作为作用域内的错误被
挂在 `graph[].error` 上。Agent 既能拿到有用数据，又能准确看到环在哪里
闭合。

---

## 运行

```bash
dotnet run --project demos/nwp-graph-walk
```

需要 .NET 10 SDK。5 个节点和 Agent 客户端全跑在一个进程里，不会监听
127.0.0.1 以外的地址。

---

## 目录结构

```
demos/nwp-graph-walk/
├── Program.cs                 # 5 个 loopback 节点 + 4 个场景（A/B/C/D）
├── NodeHosts.cs               # WebApplication 工厂（Memory / Complex）
├── InMemoryProviders.cs       # StaticMemoryNodeProvider + StaticComplexNodeProvider
├── DemoData.cs                # schema + 固定行（orders/customers/products/hubs）
└── NPS.Demo.GraphWalk.csproj  # .NET 10 Web SDK
```

---

## 仅演示用配置

演示走的是 loopback 上的 `http://`。两个 `ComplexNodeOptions` 被放宽了才
能让遍历跑起来：

| 选项 | 演示值 | 生产默认 | 原因 |
|---|---|---|---|
| `RejectPrivateChildUrls` | `false` | `true` | 生产禁止访问 loopback / RFC1918（SSRF 防护）。 |
| `AllowHttpChildUrls` | `true` | `false` | NPS-2 §13.2 强制子节点使用 `https://`。 |

`AllowedChildUrlPrefixes` 白名单仍然生效（`["http://127.0.0.1:"]`）——演示
**没有**关掉白名单，只是放松了协议头。

---

## 限制

- **未集成 NIP。** `X-NWP-Agent` 只是一个裸 URN，没有证书链、能力检查或
  速率限制。
- **深度 = 5 的硬顶未单独演示。** NPS-2 §11 规定不管 `graph_max_depth` 怎么
  设，整条路径不得超过 5 层——这条由单测覆盖，不在这个演示里。
- **进程内拓扑。** 5 个节点共享同一个进程、同一个 `HttpClientFactory`。
  生产环境的 Complex Node 是跟独立节点、通过 HTTPS + 真证书通信的。
