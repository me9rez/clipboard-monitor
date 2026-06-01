# API 参考

## `ClipboardMonitor` 类

```ts
import ClipboardMonitor from './src/clipboardMonitor.js';
import type { ClipboardMessage } from './src/types.js';
```

### `constructor(exePath: string, args?: string[])`

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `exePath` | `string` | ✅ | C# sidecar EXE 绝对路径 |
| `args` | `string[]` | ❌ | 传递给子进程的额外参数，默认 `[]` |

```ts
const monitor = new ClipboardMonitor(
  './native/bin/Release/net10.0-windows/ClipboardMonitor.exe'
);
```

### `start(): void`

启动子进程并建立 stdout JSON Stream 监听。

- `stderr` 会被实时透传到当前进程 `stderr`（含 `[DIAG]` 诊断日志）
- 子进程退出时会触发 `console.warn`

```ts
monitor.start();
```

### `stop(): void`

关闭子进程 `stdin`（发送 EOF），触发 C# 侧的 `MonitorStdin` 线程检测到后安全退出。无残留孤儿进程。

```ts
monitor.stop();
```

### `handleMessage(msg: ClipboardMessage): void`

默认消息分发器。可根据业务需要重写或包装：

```ts
const original = monitor.handleMessage.bind(monitor);
monitor.handleMessage = (msg: ClipboardMessage) => {
  // 自定义业务逻辑
  if (msg.type === 'text') {
    console.log('收到文本:', msg.payload);
  }
  // 调用原始方法（打印到控制台）
  return original(msg);
};
```

### `processAndCleanImage(tempFilePath: string): Promise<void>`

读取临时图片文件到内存，然后异步删除。如果文件不存在则静默跳过。

```ts
await monitor.processAndCleanImage('C:\\temp\\clip.png');
```

## 类型定义

```ts
// src/types.ts
export interface TextMessage       { type: 'text';       payload: string; }
export interface FilesMessage      { type: 'files';      payload: string[]; }
export interface ImagePathMessage  { type: 'image_path'; payload: string; }
export interface ErrorMessage      { type: 'error';      payload: string; }

export type ClipboardMessage =
  | TextMessage
  | FilesMessage
  | ImagePathMessage
  | ErrorMessage;
```

## 各运行时使用示例

### Node.js

```ts
import ClipboardMonitor from './src/clipboardMonitor.js';

const monitor = new ClipboardMonitor('./native/ClipboardMonitor.exe');
monitor.start();
```

### Bun

Bun 完全兼容 Node.js `child_process` API，代码无需修改：

```ts
import ClipboardMonitor from './src/clipboardMonitor.js';
const monitor = new ClipboardMonitor('./native/ClipboardMonitor.exe');
monitor.start();
```

### Deno

Deno 支持 Node.js 兼容层：

```ts
import ClipboardMonitor from './src/clipboardMonitor.js';
const monitor = new ClipboardMonitor('./native/ClipboardMonitor.exe');
monitor.start();
```

### Electron（Main Process）

```ts
import { app } from 'electron';
import path from 'path';
import ClipboardMonitor from './src/clipboardMonitor.js';

const exePath = path.join(process.resourcesPath, 'ClipboardMonitor.exe');
const monitor = new ClipboardMonitor(exePath);

app.whenReady().then(() => monitor.start());
app.on('before-quit', () => monitor.stop());
```

### NW.js

```ts
import path from 'path';
import ClipboardMonitor from './src/clipboardMonitor.js';

const exePath = path.join(nw.__dirname, 'native/ClipboardMonitor.exe');
const monitor = new ClipboardMonitor(exePath);

monitor.start();
nw.App.on('close', () => monitor.stop());
```
