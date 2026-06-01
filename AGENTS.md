# ClipboardMonitor Agent Notes

## 项目结构

- `src/clipboardMonitor.js` — 唯一 JS 库入口（`default export class ClipboardMonitor`）
- `native/Program.cs` — C# sidecar 唯一源文件
- `native/ClipboardMonitor.csproj` — C# 主项目（`net10.0-windows`，`Exe`）
- `native/ClipboardMonitorAssemblyInfo.cs` — 暴露 internals 给测试程序集
- `native/ClipboardMonitor.Tests/` — xUnit 测试项目
- `ClipboardMonitor.slnx` — 解决方案（**slnx 新格式**，不是 .sln）
- `tests/clipboardMonitor.test.js` + `tests/fake-clipboard-monitor.js` — vitest 集成/单元测试
- `example/basic-usage.js` — 演示脚本

## 核心架构

- JS 进程 `child_process.spawn` 启动 C# EXE，stdout 按行 JSON 协议
- 消息类型：`text` / `files` / `image_path` / `error`，优先级 `files > image_path > text`
- 进程退出靠**关闭 stdin**（不是 `kill`），C# 端 `MonitorStdin` 线程检测 EOF 后 `Environment.Exit(0)`
- 图片消息：JS 侧消费后必须 `unlink` 临时 PNG，否则磁盘膨胀
- 平台：**仅 Windows x64**；C# `TargetFramework=net10.0-windows`

## 命令

### JS 侧

- `pnpm install` — 拉 vitest 4.x
- `pnpm test` — 跑 vitest（**不需要**已构建的 EXE）

### C# 侧

- `cd native && dotnet build -c Release` — 普通开发构建，产物 `native/bin/Release/net10.0-windows/ClipboardMonitor.exe`
- `dotnet test native/ClipboardMonitor.Tests/ClipboardMonitor.Tests.csproj -c Release` — 跑 xUnit
- `pnpm build:native` 或 `cd native && dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -p:PublishAot=true` — AOT 单文件发布到 `native/bin/Release/net10.0-windows/win-x64/publish/`

注：xUnit 集成测试在 EXE 缺失时会自动 `dotnet build`，但要求 `dotnet` 在 PATH。

### 演示

- `node example/basic-usage.js` — **需先** `dotnet build -c Release`；脚本硬编码 `native/bin/Release/net10.0-windows/ClipboardMonitor.exe` 路径

## 约定与陷阱

- 主项目 csproj 已 `<Compile Remove="ClipboardMonitor.Tests\**\*.cs" />`，**不要**把测试代码放进 `native/` 根目录
- AOT 标志（`PublishAot` 等）**不要**写进 csproj —— 仅命令行传，避免 build/test 阶段 restore 沉重 runtime packs
- 子进程退出靠关闭 stdin，**不要**用 `kill`
- JS 测试用 `tests/fake-clipboard-monitor.js` 作为 Node 子进程替身，**不要**为单元测试构建 EXE
- C# 诊断日志用 `Console.Error.WriteLine`（带 `[DIAG]` 前缀），被 JS 透传到 stderr
- 无 lint / format / typecheck 脚本

## 调试

- 启动 `example/basic-usage.js` 后缺 `[DIAG] 进入消息循环...` → 检查 admin 权限、杀毒软件、窗口类名冲突
- 子进程残留 → 检查是否调用 `monitor.stop()`（关闭 stdin），不要用 `kill`

## 测试约定

改完代码后跑两边：`pnpm test` 和 `dotnet test native/ClipboardMonitor.Tests/ClipboardMonitor.Tests.csproj -c Release`。
