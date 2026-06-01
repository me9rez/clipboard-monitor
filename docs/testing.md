# 测试规范

## 测试分层

| 层级 | 内容 | 命令 |
|------|------|------|
| C# 单元测试 | JSON 序列化、进程生命周期 | `dotnet test` |
| JS/TS 单元测试 | handleMessage 分发、文件清理、子进程集成 | `npm test` |
| 端到端 | 运行 `example/basic-usage.ts` 并手动复制内容 | `npx tsx example/basic-usage.ts` |
| 类型检查 | TS 静态类型检查 | `pnpm typecheck` |

## C# 测试（xUnit）

### 消息序列化测试

验证 `SendJson` 输出的四种 JSON 结构正确，Unicode 无乱码：

- `text` / `files` / `image_path` / `error` 四种类型
- 中文、emoji、特殊字符保留测试

### 进程生命周期集成测试

验证子进程在 stdin 关闭后自动退出（防止孤儿进程）：

- `Process_Exits_When_Stdin_Closed`
- `Process_Exits_When_Stdin_Pipe_Broken`

## JS/TS 测试（Vitest）

### 集成测试（真实子进程）

使用 `tests/fake-clipboard-monitor.ts` 替身脚本替代真实 C# 进程：

- 启动子进程并接收全部四种消息类型
- `stop()` 触发优雅退出

### 单元测试（纯函数）

- `handleMessage` text/files/error 分发
- `processAndCleanImage` 临时文件读取与异步删除
- 文件不存在时跳过清理

### 测试隔离

`afterEach` 中必须等待子进程完全退出，避免 stdout 数据被下一个测试捕获：

```ts
afterEach(async () => {
  monitor?.stop?.();
  if (monitor?.child && !monitor.child.killed) {
    await new Promise<void>((r) => monitor.child!.once('exit', r));
  }
});
```

## 门禁要求

任何代码修改必须通过以下三重验证：

```bash
pnpm typecheck        # TS 类型检查零错误
npm test              # Vitest 全部通过
dotnet test           # xUnit 全部通过
```
