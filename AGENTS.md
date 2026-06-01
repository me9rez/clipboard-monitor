# AGENTS.md — ClipboardMonitor

> 本文档面向 AI 编码代理。人类用户请查阅 [README.md](./README.md)。

## 项目定位

高并发轻量级剪贴板监听 Native sidecar 服务。JS 侧为纯 ESM TypeScript，C# 侧为 Native AOT Console，通过 `child_process.spawn` + stdout JSON Stream 通信。

## 关键约束（必须遵守）

1. **纯 ESM + TypeScript**：所有 JS/TS 源码必须使用 ESM（`import`/`export`），禁止使用 CJS（`require`/`module.exports`）。
2. **多运行时兼容**：代码不得依赖任何特定运行时（Node.js / Bun / Deno / Electron / NW.js）的专有 API，只能使用标准 `child_process`、`readline`、`fs`。
3. **C# 侧 Win32 规范**：
   - 所有 P/Invoke 必须标注 `SetLastError = true`
   - `hInstance` 必须使用 `GetModuleHandle(null)`，禁止 `Marshal.GetHINSTANCE`
   - 每个关键 Win32 调用后必须检查返回值并输出 `[DIAG]` 诊断日志到 stderr
4. **消息协议不可变**：stdout JSON 格式（`type`/`payload`）及四种类型（`text`/`files`/`image_path`/`error`）为契约，不可随意增删字段。
5. **测试门禁**：任何代码修改必须通过 `pnpm typecheck`、`npm test`、`dotnet test` 三重验证。
6. **文件组织**：
   - `src/` — TS 源码（`clipboardMonitor.ts`、`types.ts`）
   - `native/` — C# 项目（`Program.cs`、`ClipboardMonitor.csproj`）
   - `tests/` — Vitest TS 测试
   - `example/` — 可运行的 TS 演示脚本
   - `docs/` — 详细技术文档（见下）

## 详细文档索引

| 主题 | 文档 |
|------|------|
| 架构设计 | [docs/architecture.md](./docs/architecture.md) |
| JSON Stream 协议 | [docs/protocol.md](./docs/protocol.md) |
| API 参考 | [docs/api.md](./docs/api.md) |
| C# 侧开发规范 | [docs/csharp-guidelines.md](./docs/csharp-guidelines.md) |
| 测试规范 | [docs/testing.md](./docs/testing.md) |
| 故障排查 | [docs/troubleshooting.md](./docs/troubleshooting.md) |

## 快速验证

```bash
pnpm typecheck        # TS 类型检查
npm test              # JS/TS 测试
dotnet test           # C# 测试
npx tsx example/basic-usage.ts   # 端到端演示
```
