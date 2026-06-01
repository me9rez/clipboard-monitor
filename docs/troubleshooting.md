# 故障排查指南

## 启动后无剪贴板事件输出

### 1. 检查诊断日志

运行 `npx tsx example/basic-usage.ts`，观察 `[DIAG]` 日志：

```
[DIAG] GetModuleHandle(null) = 0x7FF7C29E0000
[DIAG] RegisterClassEx 成功，atom = 0xC4BF
[DIAG] CreateWindowEx 成功，hwnd = 0x820AA0
[DIAG] AddClipboardFormatListener 成功，剪贴板监听器已注册
[DIAG] 进入消息循环...
```

**如果缺少 `[DIAG] 进入消息循环...`**：
- 检查 `RegisterClassEx` / `CreateWindowEx` 是否失败
- 确认 C# EXE 是最新版本（`dotnet build -c Release`）

### 2. 检查 `OpenClipboard` 失败

```
[DIAG] WndProc 收到 WM_CLIPBOARDUPDATE
[DIAG] ProcessClipboard 开始处理...
[DIAG] OpenClipboard 失败，Win32 错误码: 5 (0x5)
```

错误码 `5` = `ERROR_ACCESS_DENIED`。这是正常的 Windows 行为，通常由以下原因导致：
- 其他进程正在持有剪贴板锁（如大文件复制中）
- 某些安全软件拦截了剪贴板访问

**解决方案**：无需处理，下一次复制通常会成功。C# 侧已实现了 50ms 延迟来规避竞态锁。

### 3. 子进程启动后瞬间退出

```
C# 助手意外退出，Code: 143, Signal: null
```

Code `143` 表示收到 SIGTERM（由 `timeout` 或进程管理器触发）。如果不是预期行为：
- 检查 stdin 是否被意外关闭
- 检查是否有杀毒软件拦截了 EXE

## 图片保存失败

```
[DIAG] GdipCreateBitmapFromGdiDib 失败，状态码: 3
```

状态码 `3` = `InvalidParameter`。通常是剪贴板中的 DIB 格式不标准（如某些截图工具使用自定义格式）。

**解决方案**：GDI+ 无法处理所有变体 DIB，可捕获并忽略此类错误。C# 侧已实现异常抑制，不影响后续监听。

## Electron 打包后找不到 EXE

确保 `electron-builder` 将 EXE 放入 `extraResources`（而非 asar 内部）：

```json
{
  "extraResources": [
    {
      "from": "native/bin/Release/net10.0-windows/win-x64/publish/ClipboardMonitor.exe",
      "to": "ClipboardMonitor.exe"
    }
  ]
}
```

运行时路径：

```ts
const exePath = path.join(process.resourcesPath, 'ClipboardMonitor.exe');
```

## 磁盘空间增长

检查 `processAndCleanImage` 是否正常执行。如果消费方未调用 `unlink`，临时文件会堆积在 `%TEMP%` 目录。

JS 侧已实现自动清理：

```ts
await monitor.processAndCleanImage(tempFilePath);
```

## 类型检查错误

```bash
pnpm typecheck
```

常见错误：
- `Cannot find name 'process'` — 确认 `tsconfig.json` 包含 `"types": ["node"]`
- `Parameter 'err' implicitly has an 'any' type` — 给回调参数显式标注 `NodeJS.ErrnoException | null`
