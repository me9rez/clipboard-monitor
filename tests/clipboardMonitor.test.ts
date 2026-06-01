import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { fileURLToPath } from 'url';
import { dirname, join } from 'path';
import { existsSync, writeFileSync, mkdtempSync } from 'fs';
import { tmpdir } from 'os';
import ClipboardMonitor from '../src/clipboardMonitor.js';
import type { ClipboardMessage } from '../src/types.js';

const __dirname = dirname(fileURLToPath(import.meta.url));

describe('ClipboardMonitor', () => {
  let monitor: ClipboardMonitor;

  afterEach(async () => {
    monitor?.stop?.();
    // 等待子进程完全退出，避免 stdout 数据被下一个测试捕获
    if (monitor?.child && !monitor.child.killed) {
      await new Promise<void>((r) => monitor.child!.once('exit', r));
    }
  });

  it('starts child process and receives messages via stdout', async () => {
    const fakeScript = join(__dirname, 'fake-clipboard-monitor.ts');
    monitor = new ClipboardMonitor(process.execPath, [fakeScript]);

    const messages: ClipboardMessage[] = [];
    const originalHandleMessage = monitor.handleMessage.bind(monitor);
    monitor.handleMessage = (msg: ClipboardMessage) => {
      messages.push(msg);
      return originalHandleMessage(msg);
    };

    monitor.start();
    await new Promise((r) => setTimeout(r, 600));

    expect(messages.some((m) => m.type === 'text')).toBe(true);
    expect(messages.some((m) => m.type === 'files')).toBe(true);
    expect(messages.some((m) => m.type === 'image_path')).toBe(true);
    expect(messages.some((m) => m.type === 'error')).toBe(true);
  });

  it('stops by ending stdin and child exits', async () => {
    const fakeScript = join(__dirname, 'fake-clipboard-monitor.ts');
    monitor = new ClipboardMonitor(process.execPath, [fakeScript]);
    monitor.start();

    expect(monitor.child).not.toBeNull();
    expect(monitor.child!.killed).toBe(false);

    monitor.stop();
    await new Promise((r) => setTimeout(r, 200));

    expect(monitor.child).toBeNull();
  });

  it('handleMessage dispatches text correctly', () => {
    monitor = new ClipboardMonitor('dummy.exe');
    const logs: unknown[][] = [];
    const originalLog = console.log;
    console.log = (...args: unknown[]) => logs.push(args);

    monitor.handleMessage({ type: 'text', payload: 'test text' });

    console.log = originalLog;
    expect(logs.some((args) => (args[0] as string)?.includes?.('剪贴板文本') && args.includes('test text'))).toBe(true);
  });

  it('handleMessage dispatches files correctly', () => {
    monitor = new ClipboardMonitor('dummy.exe');
    const logs: unknown[][] = [];
    const originalLog = console.log;
    console.log = (...args: unknown[]) => logs.push(args);

    monitor.handleMessage({ type: 'files', payload: ['a.txt', 'b.txt'] });

    console.log = originalLog;
    expect(logs.some((args) => (args[0] as string)?.includes?.('剪贴板文件列表'))).toBe(true);
  });

  it('handleMessage dispatches error correctly', () => {
    monitor = new ClipboardMonitor('dummy.exe');
    const errors: unknown[][] = [];
    const originalError = console.error;
    console.error = (...args: unknown[]) => errors.push(args);

    monitor.handleMessage({ type: 'error', payload: 'boom' });

    console.error = originalError;
    expect(errors.some((args) => (args[0] as string)?.includes?.('C# 内部错误') && args.includes('boom'))).toBe(true);
  });

  it('processAndCleanImage reads and deletes temp file', async () => {
    const tempDir = mkdtempSync(join(tmpdir(), 'clip-test-'));
    const tempFile = join(tempDir, 'test.png');
    writeFileSync(tempFile, Buffer.from('fake-image-data'));

    monitor = new ClipboardMonitor('dummy.exe');

    const logs: unknown[][] = [];
    const originalLog = console.log;
    console.log = (...args: unknown[]) => logs.push(args);

    await monitor.processAndCleanImage(tempFile);

    console.log = originalLog;
    expect(existsSync(tempFile)).toBe(false);
    expect(logs.some((args) => (args[0] as string)?.includes?.('读取完毕'))).toBe(true);
  });

  it('processAndCleanImage skips when file does not exist', async () => {
    monitor = new ClipboardMonitor('dummy.exe');
    const logs: unknown[][] = [];
    const originalLog = console.log;
    console.log = (...args: unknown[]) => logs.push(args);

    await monitor.processAndCleanImage('C:\\nonexistent\\file.png');

    console.log = originalLog;
    expect(logs.some((args) => (args[0] as string)?.includes?.('读取完毕'))).toBe(false);
  });
});
