[English Version](./README.md) | 中文版

# Bridge Playground 演示

同一个 NWP 动作——`greetings.hello(name)`——通过**三个不同的兼容桥**
（MCP / A2A / gRPC）同时对外暴露。三个桥前面挂的是同一个上游 Action Node、
同一份业务代码；只有对外的 wire format 不同。一个进程内的 .NET 客户端依次
跑三条通道。

```
                      ┌──────────────────────────────┐
                      │  NWP Action Node  :17481     │
                      │  greetings.hello(name)       │
                      └──────────────▲───────────────┘
                                     │ POST /invoke  (ActionFrame)
         ┌───────────────────────────┼────────────────────────────┐
         │                           │                            │
┌────────┴────────┐        ┌─────────┴────────┐         ┌─────────┴────────┐
│ MCP 桥          │        │ A2A 桥           │         │ gRPC 桥          │
│   :17482/mcp    │        │   :17483/a2a     │         │   :17484 h2c     │
│ tools/call      │        │ tasks/send       │         │ Invoke RPC       │
└─────────────────┘        └──────────────────┘         └──────────────────┘
         ▲                           ▲                            ▲
         │                           │                            │
         └────────────┐     ┌────────┘                            │
                      │     │                                     │
                  ┌───┴─────┴──────────────────┐                  │
                  │   一个 .NET 客户端          │──────────────────┘
                  │   (Program.cs)              │
                  └─────────────────────────────┘
```

---

## 原理 —— 三个桥到底在干什么

NWP 是 Agent 和节点之间的标准 wire format，但现有的 Agent 生态大部分说的
不是 NWP——而是 Anthropic MCP、Google A2A、或者裸 gRPC。每个桥本质上都是
一个**形状翻译器**，挂在一个未经改动的 NWP 节点前面，把它暴露成：

| 桥 | 对外暴露 | 载荷载体 |
|---|---|---|
| `LabAcacia.McpBridge` | MCP 2024-11-05 服务端：`tools/list` / `tools/call` | CapsFrame 序列化后塞进 `content[{type:"text", text:"..."}]` |
| `LabAcacia.A2aBridge` | Google A2A v0.2 服务端：`tasks/send` / `tasks/get` | CapsFrame 内联为 `artifacts[].parts[{type:"data", data:…}]` |
| `LabAcacia.GrpcBridge` | gRPC 服务 `NwpBridge.Invoke`（本演示跑 h2c） | CapsFrame 序列化成 JSON → 挂进 `bytes body_json` |

桥**只改信封**，所以每次到达上游的都是同一个 `ActionFrame`，每次跑的都是
同一个 `greetings.hello` 提供者，每次吐出来的都是同一个 CapsFrame
（除了提供者自己写进去的 `via` 标签之外，字节级一致）。三个桥都复用 NWP
的 anchor ref、错误帧、幂等键——没有任何一个桥在重新发明这些东西。

---

## 作用 —— 这个演示的意义

协议生态是混乱的。MCP 和 A2A 已经扎根在 Agent 工具链里；gRPC 只要有人说
"给我一个强类型 RPC" 基本就是默认选择。NPS 要被采用，就得正面回答一个
问题：

> **我能不能继续用我现成的 MCP / A2A / gRPC 客户端，同时让服务端是 NWP？**

答案是"能，而且不用二选一"。这个演示就是最短的一个证明：一个上游、三个
并存的前置桥、一个客户端把三条路都走一遍，能看见的结果是每次都拿到同一个
CapsFrame——只是按每种 wire format 重新包了一层。

---

## 演示了什么

1. **一个 NWP 动作，三个协议外观。** 上游 Action Node 只有一份
   `IActionNodeProvider` 实现；MCP / A2A / gRPC 都只是翻译成 `POST /invoke`。
2. **形状翻译是纯机械的。** 桥不发明业务逻辑——就是换信封、透传。内层
   CapsFrame 的 `anchor_ref`、`count`、`data[]`、`token_est` 保持不变。
3. **桥是可组合的。** 三个桥在同一个进程里跑、都指向同一个上游。生产里
   通常每个桥单独部署，但 NWP 侧完全不用变。

---

## 运行结果（实际运行输出，2026-04-21）

**场景 A —— 走 MCP。** `tools/call` 调用 `greetings__greetings_hello`：

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

MCP 按 2024-11-05 规范把 CapsFrame 放进 `content` 文本块。内层载荷仍是
一个标准的 NPS CapsFrame（frame_type 4）。

**场景 B —— 走 A2A。** `tasks/send`，skillId `greetings.hello`：

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

A2A 把 CapsFrame 以一个类型化的 `data` part 挂进 `artifacts[]`。
`anchor_ref` 和载荷都一样——只有 Task / Artifact 信封是 A2A 风味的。

**场景 C —— 走 gRPC。** `NwpBridge.Invoke` 跑在 h2c 上：

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

gRPC 桥走字节级透传：`InvokeResponse.body_json` 里就是原封不动的
CapsFrame 序列化结果。

**解读。** 三个 `via`（`MCP` / `A2A` / `gRPC`）是*唯一*有意义的差异
——提供者把 `via` 写进响应，你就能知道任意一条响应是从哪条通道出去的。
三次的 `anchor_ref` 完全一样，这意味着 Agent 可以缓存一次，无论后续
通过哪个桥拿响应都能命中。

---

## 运行

```bash
dotnet run --project demos/bridge-playground
```

需要 .NET 10 SDK。4 个 Kestrel host（上游 + 3 个桥）和客户端全部在一个
进程里，不会监听 127.0.0.1 以外的地址。

---

## 目录结构

```
demos/bridge-playground/
├── Program.cs                      # 4 个 host + 3 个客户端场景（A/B/C）
├── HostBuilders.cs                 # Kestrel 工厂：上游 + 3 个桥
├── GreetingsProvider.cs            # greetings.hello 的 IActionNodeProvider 实现
├── Protos/nwp_bridge_client.proto  # 桥 proto 的本地拷贝（仅 Client）
└── NPS.Demo.BridgePlayground.csproj
```

---

## 为什么要本地复制一份 `.proto`

`LabAcacia.GrpcBridge` 发出去的 proto 是 `GrpcServices="Server"`，只生成
服务端基类。演示需要**客户端 stub**，同时又引用了桥库作为服务端——如果
从同一份 proto 同时生成两侧 stub，就会在
`LabAcacia.GrpcBridge.Generated.*` 下撞消息类型名。解决方式是把 proto
本地复制一份，改成 `csharp_namespace = "NPS.Demo.BridgePlayground.Grpc"` +
`GrpcServices="Client"`。wire format 完全一样，只是 .NET 命名空间不同，
因此两侧透明互通。

---

## 仅演示用配置

- **gRPC 用明文 h2c。** 为了让演示不用证书也能跑，gRPC 桥监听 `http://`。
  `System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport` AppContext
  开关只在本演示的入口点打开。**生产 gRPC 必须走 TLS。**
- **未集成 NIP。** 桥调用不带 `X-NWP-Agent`、不带证书链、不做能力检查。
  上游接受匿名调用是因为 `ActionNodeOptions.RequireAuth` 默认 `false`。

---

## 限制

- **仅单个动作。** 演示只打了一个动作。MCP `resources/*`（对 Memory Node
  的映射）、A2A 带多个 parts 的 `artifacts[]`、gRPC `Query` RPC 都没演示
  ——要看完整面，请参考 `compat/{mcp,a2a,grpc}-bridge/` 的单测。
- **进程内 host。** 4 个 Kestrel host 共享一个进程、一个默认 socket handler。
  生产环境的桥是独立部署的。
- **仅同步路径。** `greetings.hello` 是 `Async=false`；202 异步路径
  （A2A `submitted → working → completed` 轮询、gRPC 异步）在这里没跑。
