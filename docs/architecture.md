# 架构设计

## 整体架构

本项目采用 **Sidecar（侧车/伴生进程）** 架构，将系统级剪贴板监听逻辑下沉到独立的 Native 进程中，前端仅负责进程管理和数据消费。

```
┌─────────────────────────────────────────────────────────────┐
│  主进程 (Node.js / Bun / Deno / Electron / NW.js)           │
│  ────────────────────────────────────────────────────────   │
│  ClipboardMonitor (src/clipboardMonitor.ts)                 │
│  ├── spawn C# EXE  via  child_process                       │
│  ├── readline 解析 stdout JSON Stream                       │
│  ├── handleMessage 分发 text / files / image_path / error   │
│  └── processAndCleanImage 读取 & 清理临时 PNG               │
└────────────────────────┬────────────────────────────────────┘
                         │ stdin-pipe / stdout-pipe / stderr-pipe
┌────────────────────────┴────────────────────────────────────┐
│  Sidecar (C# Native AOT Console)                            │
│  ────────────────────────────────────────────────────────   │
│  ├── HWND_MESSAGE 隐藏窗口                                  │
│  ├── AddClipboardFormatListener 注册系统剪贴板监听          │
│  ├── Win32 Message Pump (GetMessage/Translate/Dispatch)     │
│  ├── WndProc 捕获 WM_CLIPBOARDUPDATE                        │
│  ├── OpenClipboard → 读取 CF_HDROP / CF_DIB / CF_TEXT       │
│  └── Console.WriteLine(JSON) → stdout                       │
└─────────────────────────────────────────────────────────────┘
```

## 设计决策

### 为什么用 Sidecar 而不是直接集成？

| 方案 | 问题 |
|------|------|
| Electron 主进程直接调用 Win32 API | 需要 `node-gyp` 原生模块，跨版本兼容性差，Electron 升级时经常断裂 |
| 使用 `node-clipboardy` 等轮询库 | 基于定时轮询，延迟高、CPU 占用高、无法监听文件类型 |
| **Sidecar 独立进程** | 零原生依赖、进程隔离、崩溃不影响主进程、可独立升级替换 |

### 为什么选择 C# Native AOT？

- **体积**：publish 后仅 ~1.5MB~2MB，独立运行无需 .NET 运行时
- **启动速度**：AOT 编译后冷启动 < 100ms
- **Win32 互操作**：C# 的 P/Invoke 比 Node-API / C++ addon 开发效率高一个数量级
- **稳定性**：不依赖 V8 / Node 版本，Electron 升级时 sidecar 完全不受影响

### 为什么选择 JSON Stream 而不是 IPC/TCP？

- **零配置**：stdout 天然存在，无需端口、无需协议协商
- **跨运行时**：所有 JS 运行时（包括 Deno）都支持 `child_process.spawn` + stdout
- **可观测性**：直接 `node example/basic-usage.ts` 就能看到实时 JSON 流，便于调试
- **低延迟**：按行解析，`readline` 在底层使用 Buffer 流式读取，无阻塞

## 进程生命周期

```
主进程 start()
    │
    ▼
spawn(C# EXE) ──► C# 进程启动
    │                  │
    │                  ├── 初始化 GDI+
    │                  ├── 注册 HWND_MESSAGE 窗口
    │                  ├── AddClipboardFormatListener
    │                  └── 启动 GetMessage 消息循环
    │
    ├── stdout ◄─────── 收到剪贴板变化时输出 JSON
    ├── stderr ◄─────── [DIAG] 诊断日志
    └── stdin ──────── 主进程退出时关闭 stdin
                              │
                              ▼
                         C# MonitorStdin 线程
                         检测到 EOF → Environment.Exit(0)
                              │
                              ▼
                         子进程安全退出，无孤儿进程
```

## 内存与句柄安全

- `OpenClipboard` 均配对 `CloseClipboard`
- `GlobalLock` 均配对 `GlobalUnlock`
- GDI+ 位图通过 `GdipDisposeImage` 显式释放
- 临时图片文件在消费后立即 `unlink`，防止磁盘膨胀
