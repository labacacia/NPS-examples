[English Version](./README.md) | 中文版

# NPS Examples

[![License](https://img.shields.io/badge/license-Apache%202.0-blue)](./LICENSE)
[![Suite](https://img.shields.io/badge/suite-v1.0.0--alpha.14-orange.svg)](https://gitee.com/labacacia/NPS-Release/releases/tag/v1.0.0-alpha.14)
[![NWP](https://img.shields.io/badge/NWP-v0.14-4af0b0.svg)]()

[Neural Protocol Suite (NPS)](https://github.com/labacacia/nps) 的精选可运行
演示集。每个演示都是针对某一个协议机制的端到端自包含场景：你可以读源码、
读 README——并且在有 NPS 主仓库 checkout 的情况下把它跑起来。

> **代码的唯一源。** 这些演示同时存在于主 NPS 仓库的 `demos/` 目录下。
> 本仓库的存在是为了让这些演示在单独搜索时也能被找到，而不需要把整个套件
> clone 下来。要**运行**演示，请 clone
> [`labacacia/nps`](https://github.com/labacacia/nps) 并在那边发起调用——
> 各个 `.csproj` 文件通过 `ProjectReference` 引用 NPS 包
> （`NPS.Core`、`NPS.NWP`、`LabAcacia.McpIngress`、`LabAcacia.A2aIngress`、
> `LabAcacia.GrpcIngress`），在这些包正式发布到 NuGet 之前，这些路径只在
> 主仓库里才能解析。

---

## 演示列表

| # | 演示 | 一句话说明 | 演练的协议点 |
|---|------|------------|--------------|
| 1 | [**nwp-graph-walk**](./nwp-graph-walk/README.cn.md) | Complex Node 沿着带标签的 ref 遍历到其他 NWP 节点，带深度上限 + 环路检测 | NPS-2 (NWP) §11 `graph.refs`、`X-NWP-Depth`、`X-NWP-Trace` |
| 2 | [**ingress-playground**](./ingress-playground/README.cn.md) | 同一个 NWP Action Node 通过 MCP、A2A、gRPC 三个桥同时对外暴露 | `LabAcacia.McpIngress` + `LabAcacia.A2aIngress` + `LabAcacia.GrpcIngress` |
| 3 | [**cross-sdk-interop**](./cross-sdk-interop/README.cn.md) | 同一个 .NET NWP Memory Node 在 dotnet / python / nodejs / go 四个客户端面前返回逐字节一致的响应（仅用标准库 HTTP + JSON） | NWP `/query` 的 JSON-Overlay 形态、CapsFrame 跨客户端字节级一致 |

每个演示的 README 都包含四部分：

1. **原理** —— 被演练的 NPS 规范机制。
2. **作用** —— 演示要回答什么问题。
3. **演示了什么** —— 具体可观察的行为。
4. **运行结果** —— 来自一次真实运行的输出，带解读。

---

## 如何运行

```bash
git clone https://github.com/labacacia/nps.git
cd nps

# 演示 1 —— NWP 图谱遍历（单进程、5 个 loopback 节点、4 个场景）
dotnet run --project demos/nwp-graph-walk

# 演示 2 —— bridge playground（上游 + 3 个桥 + 客户端全在一个进程）
dotnet run --project demos/ingress-playground

# 演示 3 —— 跨 SDK 互操作（启动服务端、分发给 PATH 上的每个运行时）
bash demos/cross-sdk-interop/run.sh
```

需要 .NET 10 SDK。演示 3 还会额外探测 `python3`、`node`、`go` 是否在
`PATH` 上，缺失的自动跳过。

---

## 为什么要单开一个仓库

三个理由，按重要性排序：

1. **可发现性。** 有人搜 "NPS MCP ingress example"、"NWP graph walk demo"
   能直接找到一个自描述的仓库，而不是一个藏在主仓库三级目录之下的子目录。
2. **更小的 clone 面积。** 主仓库包含完整规范、6 个 SDK、3 个桥、6 份 NIP
   CA 实现。你不需要 clone 这些只为了读 150 行 Program.cs。
3. **稳定的展示面。** 主仓库 HEAD 一直在动——协议版本升级、测试添加、
   草稿合并。单独的 examples 仓库可以独立打 tag 来匹配特定的 NPS 发布
   里程碑。

---

## 许可证

Apache-2.0。见 [`LICENSE`](./LICENSE) 和 [`NOTICE`](./NOTICE)。

## Issue 与贡献

- 针对**演示本身**的问题（错别字、解释不清楚、输出过时）请在本仓库开 Issue。
- 协议 bug 或规范讨论请在 [`labacacia/nps`](https://github.com/labacacia/nps/issues)
  开 Issue——这些 bug 的源头在上游，不在这里。
- 分支 / PR 流程见 [`CONTRIBUTING.cn.md`](./CONTRIBUTING.cn.md)。
