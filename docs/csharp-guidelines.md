# C# 侧开发规范

## 目标框架

- `TargetFramework: net10.0-windows`
- `OutputType: Exe`

## Native AOT 发布

Build 阶段**不启用** AOT 属性（避免 restore 沉重的 runtime packs），仅在 publish 时通过命令行传入：

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:PublishTrimmed=true -p:PublishAot=true
```

### JSON 序列化必须走源生成（AOT 硬约束）

Native AOT 下 `JsonSerializer.IsReflectionEnabledByDefault = false`，反射式序列化在**运行时**抛
`InvalidOperationException: Reflection-based serialization has been disabled for this application`。
编译期只报 `IL2026`/`IL3050` 警告，不阻断发布，因此极易带着“能编译但一条消息都发不出去”的产物上线
（历史上 `SendJson` 的 `catch { }` 会把异常吞掉，表现为 stdout 永久为空）。

- 禁止 `JsonSerializer.Serialize(匿名对象)` / `JsonSerializer.Serialize(object)`。
- 消息类型定义在 `Messages.cs`（`StringPayloadMessage` / `FileListMessage`），并注册进 `ClipboardJsonContext`。
  新增消息类型时**必须**同步加 `[JsonSerializable(typeof(...))]`，否则 AOT 下会退回反射路径。
- 不要用 `-p:JsonSerializerIsReflectionEnabledByDefault=true` 兜底：实测不抛异常，但匿名类型属性被裁剪，
  输出静默变成 `{}`，比报错更难定位。
- 发布后必须确认 **0 条 `IL2026`/`IL3050` 警告**，并做一次真机剪贴板端到端验证。

```
default (AOT)          → 反射+匿名类型: InvalidOperationException；源生成: OK
switch=true (AOT)      → 反射+匿名类型: "{}"（静默错误）；源生成: OK
```

## Win32 P/Invoke 规范

### 必须标注 `SetLastError = true`

```csharp
[DllImport("user32.dll", SetLastError = true)]
private static extern bool AddClipboardFormatListener(IntPtr hwnd);
```

### hInstance 必须使用 `GetModuleHandle(null)`

**禁止**使用 `Marshal.GetHINSTANCE(typeof(Program).Module)` — 在 .NET Core/.NET 5+ 中返回 `-1`。

```csharp
[DllImport("kernel32.dll")]
private static extern IntPtr GetModuleHandle(string? lpModuleName);

// 使用
IntPtr hInstance = GetModuleHandle(null);
```

### 关键调用后必须检查返回值并输出诊断日志

```csharp
bool added = AddClipboardFormatListener(hwnd);
if (!added)
{
    int err = Marshal.GetLastWin32Error();
    LogDiag($"AddClipboardFormatListener 失败，Win32 错误码: {err} (0x{err:X})");
    return;
}
LogDiag("AddClipboardFormatListener 成功");
```

## 诊断日志规范

- 统一使用 `Console.Error.WriteLine` 输出，前缀 `[DIAG]`
- 便于 JS 侧通过 `child.stderr` 管道实时捕获

```csharp
private static void LogDiag(string message)
{
    Console.Error.WriteLine($"[DIAG] {message}");
    Console.Error.Flush();
}
```

## 消息泵健壮性

`GetMessage` 可能返回 `-1`（错误），必须显式处理：

```csharp
int result;
while ((result = GetMessage(out msg, IntPtr.Zero, 0, 0)) != 0)
{
    if (result == -1)
    {
        int err = Marshal.GetLastWin32Error();
        LogDiag($"GetMessage 返回 -1，错误码: {err}");
        break;
    }
    TranslateMessage(ref msg);
    DispatchMessage(ref msg);
}
```

## GDI+ 资源管理

```csharp
// 必须显式释放位图句柄
try
{
    // ... GdipCreateBitmapFromGdiDib ...
}
finally
{
    GdipDisposeImage(hBitmap);
}
```

## 剪贴板格式优先级

```
CF_HDROP (files) > CF_DIB (image_path) > CF_UNICODETEXT (text)
```

单次变化只发送一种类型。`OpenClipboard` 失败后必须输出日志，不可静默忽略。
