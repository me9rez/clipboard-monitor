/**
 * ClipboardMonitor 使用演示
 *
 * 本脚本展示如何在 Node.js / Electron 环境中启动剪贴板监听 sidecar 服务，
 * 并接收来自 C# Native 助手的 JSON 消息。
 *
 * 前置条件:
 *   cd native && dotnet build -c Release
 *
 * 运行方式:
 *   npx tsx example/basic-usage.ts
 *
 * 启动后，在 Windows 中复制任意文本、图片或文件，观察终端输出。
 */

import ClipboardMonitor from '../src/clipboardMonitor.js';
import type { ClipboardMessage } from '../src/types.js';
import { fileURLToPath } from 'url';
import { dirname, join } from 'path';
import { existsSync } from 'fs';

const __dirname = dirname(fileURLToPath(import.meta.url));

// ------------------------------------------------------------------
// 1. 确定 sidecar 可执行文件路径
// ------------------------------------------------------------------
const exePath = join(__dirname, '../native/bin/Release/net10.0-windows/ClipboardMonitor.exe');

if (!existsSync(exePath)) {
  console.error('❌ 错误: 找不到 C# Native sidecar 可执行文件');
  console.error('   路径:', exePath);
  console.error('   请先编译项目:');
  console.error('   cd native && dotnet build -c Release\n');
  process.exit(1);
}

// ------------------------------------------------------------------
// 2. 实例化 ClipboardMonitor
// ------------------------------------------------------------------
const monitor = new ClipboardMonitor(exePath);

// 拦截 handleMessage 以便在终端打印收到的结构化数据
const originalHandleMessage = monitor.handleMessage.bind(monitor);
monitor.handleMessage = (msg: ClipboardMessage) => {
  const timestamp = new Date().toLocaleTimeString();
  console.log(`[${timestamp}] 📥 收到消息:`, JSON.stringify(msg));
  return originalHandleMessage(msg);
};

// ------------------------------------------------------------------
// 3. 启动监听
// ------------------------------------------------------------------
console.log('=== ClipboardMonitor 启动 ===');
console.log('sidecar 路径:', exePath);
console.log('提示: 在 Windows 中复制文本 / 图片 / 文件，观察下方输出');
console.log('按 Ctrl+C 停止\n');

monitor.start();

// ------------------------------------------------------------------
// 4. 优雅退出
// ------------------------------------------------------------------
process.on('SIGINT', () => {
  console.log('\n🛑 接收到中断信号，正在停止...');
  monitor.stop();
  console.log('✅ 已安全退出');
  process.exit(0);
});
