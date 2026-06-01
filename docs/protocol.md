# JSON Stream 协议规范

## 传输层

C# sidecar 通过 **stdout 按行输出 JSON**。每行一个完整的 JSON 对象，以 `\n` 结尾。JS 侧使用 `readline.createInterface({ input: child.stdout })` 按行读取并解析。

## 消息格式

所有消息均为以下四种类型之一，统一结构为 `{ type, payload }`：

```ts
type ClipboardMessage =
  | { type: 'text';       payload: string }
  | { type: 'files';      payload: string[] }
  | { type: 'image_path'; payload: string }
  | { type: 'error';      payload: string };
```

### `text` — 文本内容

```json
{"type":"text","payload":"Hello World"}
```

来源：`CF_UNICODETEXT` (format 13)

### `files` — 文件路径列表

```json
{"type":"files","payload":["C:\\Users\\Alice\\doc.pdf","D:\\backup\\photo.jpg"]}
```

来源：`CF_HDROP` (format 15)

### `image_path` — 临时图片文件路径

```json
{"type":"image_path","payload":"C:\\Users\\Alice\\AppData\\Local\\Temp\\clip_temp_638743...\\.png"}
```

来源：`CF_DIB` (format 8) → GDI+ 转换为 PNG 临时文件

**消费方责任**：读取文件后必须删除，防止磁盘膨胀。JS 侧的 `processAndCleanImage` 已实现此逻辑。

### `error` — 内部错误

```json
{"type":"error","payload":"OpenClipboard failed: 0x5"}
```

来源：C# 侧 `ProcessClipboard` 捕获的异常。不会中断监听循环。

## 格式优先级

单次剪贴板变化只会触发**一种**消息类型，优先级如下：

```
CF_HDROP (files) > CF_DIB (image_path) > CF_UNICODETEXT (text)
```

即：如果剪贴板同时包含文件和文本，只发送 `files`。

## 编码

C# 侧 `Console.OutputEncoding = Encoding.UTF8`，确保中文、emoji、特殊符号无乱码。
