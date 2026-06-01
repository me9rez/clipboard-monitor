# ClipboardMonitor

高并发轻量级剪贴板监听 Native sidecar 服务，支持 **Node.js / Bun / Deno / Electron / NW.js** 等多种运行时。

## 特性

- 🚀 **多运行时兼容**：纯 ESM 设计，零运行时绑定，可在 Node.js、Bun、Deno、Electron、NW.js 中即插即用
- 🪟 **系统级监听**：基于 Win32 `WM_CLIPBOARDUPDATE`，监听系统全局剪贴板，不依赖任何 UI 框架
- 📦 **Native AOT 编译**：C# 侧编译为 ~1.5MB 独立 EXE，无需 .NET 运行时即可运行
- 🔄 **JSON Stream 协议**：子进程 stdout 按行输出 JSON，解析简单、延迟极低
- 🖼️ **图片无损转换**：原生 GDI+ 将剪贴板 DIB 直接保存为 PNG，无中间压缩损失
- 🛡️ **孤儿进程防护**：父进程退出或 stdin 关闭时，C# 子进程自动安全退出
- 🧹 **临时文件自清理**：图片消费完毕后立即 `unlink`，防止磁盘膨胀

## 架构

```
┌─────────────────────────────────────────────────────────────┐
│  主进程 (Node.js / Bun / Deno / Electron / NW.js)           │
│  ────────────────────────────────────────────────────────   │
│  ClipboardMonitor (ESM)                                     │
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

## 目录结构

```
ClipboardMonitor/
├── native/
│   ├── ClipboardMonitor.csproj              # C# Native AOT 项目
│   ├── Program.cs                           # Win32 剪贴板监听核心
│   └── ClipboardMonitor.Tests/              # xUnit 单元测试
├── src/
│   └── clipboardMonitor.ts                  # 纯 ESM 通用包装器
├── example/
│   └── basic-usage.ts                       # 命令行演示脚本
├── tests/
│   ├── clipboardMonitor.test.ts             # Vitest 单元测试
│   └── fake-clipboard-monitor.ts            # Node 替身脚本（测试用）
├── package.tson
├── vitest.config.ts
└── README.md
```

## 快速开始

### 前置条件

- Windows 10/11 (x64)
- [.NET SDK 8.0+](https://dotnet.microsoft.com/download)（开发环境使用 10.0）
- 任一支持的 JS 运行时：Node.js 18+ / Bun / Deno / Electron / NW.js

### 1. 编译 C# Sidecar

```bash
cd native
dotnet build -c Release
```

编译产物：`native/bin/Release/net10.0-windows/ClipboardMonitor.exe`

### 2. 各运行时使用

#### Node.js

```js
import ClipboardMonitor from './src/clipboardMonitor.ts';

const monitor = new ClipboardMonitor('./native/bin/Release/net10.0-windows/ClipboardMonitor.exe');
monitor.start();

// 停止时自动安全退出子进程
monitor.stop();
```

#### Bun

```js
import ClipboardMonitor from './src/clipboardMonitor.ts';

const monitor = new ClipboardMonitor('./native/bin/Release/net10.0-windows/ClipboardMonitor.exe');
monitor.start();
```

Bun 完全兼容 Node.js `child_process` API，`clipboardMonitor.ts` 可直接运行无需修改。

#### Deno

```ts
import ClipboardMonitor from './src/clipboardMonitor.ts';

const monitor = new ClipboardMonitor('./native/bin/Release/net10.0-windows/ClipboardMonitor.exe');
monitor.start();
```

Deno 支持 Node.js 兼容层（`npm:` 或 `--compat`），`child_process.spawn` 行为一致。

#### Electron（Main Process）

```js
// main.ts / background.ts
import { app } from 'electron';
import path from 'path';
import ClipboardMonitor from './src/clipboardMonitor.ts';

const exePath = path.join(process.resourcesPath, 'ClipboardMonitor.exe');
const monitor = new ClipboardMonitor(exePath);

app.whenReady().then(() => {
    monitor.start();
});

app.on('before-quit', () => {
    monitor.stop();
});
```

**打包注意事项**：使用 `electron-builder` 时，将 `ClipboardMonitor.exe` 放入 `extraResources`，避免被打包进 asar：

```json
// electron-builder.tson
{
  "extraResources": [
    {
      "from": "native/bin/Release/net10.0-windows/ClipboardMonitor.exe",
      "to": "ClipboardMonitor.exe"
    }
  ]
}
```

#### NW.js

```js
// background.ts / node-main
import path from 'path';
import ClipboardMonitor from './src/clipboardMonitor.ts';

const exePath = path.join(nw.__dirname, 'native/ClipboardMonitor.exe');
const monitor = new ClipboardMonitor(exePath);

monitor.start();

nw.App.on('close', () => {
    monitor.stop();
});
```

### 3. 运行演示

```bash
npx tsx example/basic-usage.ts
```

启动后在 Windows 中复制任意文本/图片/文件，观察终端输出。

---

## 消息协议

C# Sidecar 通过 **stdout 按行输出 JSON**，格式如下：

| `type` | `payload` 类型 | 说明 |
|--------|---------------|------|
| `text` | `string` | 剪贴板文本内容（`CF_UNICODETEXT`） |
| `files` | `string[]` | 文件路径数组（`CF_HDROP`） |
| `image_path` | `string` | 临时 PNG 文件绝对路径（`CF_DIB` → GDI+ 转换） |
| `error` | `string` | 内部错误描述 |

**优先级**：`files` > `image_path` > `text`。单次剪贴板变化只会触发一种类型。

### 示例输出

```json
{"type":"text","payload":"Hello World"}
{"type":"files","payload":["C:\\Users\\Alice\\doc.pdf"]}
{"type":"image_path","payload":"C:\\Users\\Alice\\AppData\\Local\\Temp\\clip_temp_638743...\\.png"}
```

---

## API

### `new ClipboardMonitor(exePath, args?)`

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `exePath` | `string` | ✅ | C# Sidecar EXE 绝对路径 |
| `args` | `string[]` | ❌ | 传递给子进程的额外参数，默认 `[]` |

### `monitor.start()`

启动子进程，建立 stdout JSON Stream 监听。子进程 `stderr` 会实时透传到当前进程 `stderr`（含 `[DIAG]` 诊断日志）。

### `monitor.stop()`

关闭子进程 `stdin`，触发 C# 侧的 `MonitorStdin` 线程检测到 EOF 后安全退出。进程无残留。

### `monitor.handleMessage(msg)`

默认消息分发器，可根据业务需要重写：

```js
const original = monitor.handleMessage.bind(monitor);
monitor.handleMessage = (msg) => {
    if (msg.type === 'text') {
        // 自定义业务逻辑
        console.log('文本:', msg.payload);
    } else {
        original(msg);
    }
};
```

---

## 测试

### C# 测试

```bash
dotnet test native/ClipboardMonitor.Tests/ClipboardMonitor.Tests.csproj -c Release
```

- 5 个消息序列化测试（JSON 格式 / Unicode 兼容）
- 2 个进程生命周期集成测试（stdin 关闭 → 子进程自退）

### JS 测试

```bash
pnpm install
pnpm test
```

- 2 个子进程集成测试（真实 Node 替身脚本启动 + 优雅退出）
- 3 个 `handleMessage` 分发测试
- 2 个 `processAndCleanImage` 异步清理测试

---

## 生产发布（Native AOT）

```bash
cd native
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:PublishTrimmed=true -p:PublishAot=true
```

产出：
```
native/bin/Release/net10.0-windows/win-x64/publish/ClipboardMonitor.exe
```

- 体积约 **1.5MB~2MB**
- 可在未安装 .NET 运行时的全新 Windows 虚拟机中直接运行
- 启动时间 < 100ms

---

## 故障排查

启动 `example/basic-usage.ts` 后，终端会输出 `[DIAG]` 前缀的诊断日志：

```
[DIAG] GetModuleHandle(null) = 0x7FF7C29E0000
[DIAG] RegisterClassEx 成功，atom = 0xC4BF
[DIAG] CreateWindowEx 成功，hwnd = 0x820AA0
[DIAG] AddClipboardFormatListener 成功，剪贴板监听器已注册
[DIAG] 进入消息循环...
[DIAG] WndProc 收到 WM_CLIPBOARDUPDATE
[DIAG] 检测到 CF_UNICODETEXT 格式
```

若缺少 `[DIAG] 进入消息循环...` 或出现 `RegisterClassEx 失败` / `CreateWindowEx 失败`，请检查：
1. 是否以管理员权限运行（某些企业环境需要）
2. EXE 是否被杀毒软件拦截
3. 是否有其他程序占用了相同的窗口类名

---

## 关键技术细节

1. **无窗口消息泵**：利用 `HWND_MESSAGE` 创建隐藏消息窗口，不依赖 WinForms/WPF
2. **格式检测优先级**：`CF_HDROP`（文件）> `CF_DIB`（图片）> `CF_UNICODETEXT`（文本）
3. **图片转换**：GDI+ `GdipCreateBitmapFromGdiDib` 直接从内存 DIB 创建位图并保存为 PNG
4. **竞态锁规避**：`WndProc` 中收到 `WM_CLIPBOARDUPDATE` 后延迟 50ms 再 `OpenClipboard`，避免与大文件复制冲突
5. **主线程零阻塞**：所有剪贴板 IO、图片编码、磁盘写入均在 C# 子进程完成，JS 侧仅做无阻塞 Stream 解析

## License

MIT
