import { spawn, ChildProcess } from 'child_process';
import readline from 'readline';
import fs from 'fs';
import type { ClipboardMessage } from './types.js';

/**
 * ClipboardMonitor - 剪贴板监听 sidecar 包装器
 *
 * 纯 ESM 实现，兼容 Node.js / Bun / Deno / Electron / NW.js。
 * 通过 child_process.spawn 启动 C# Native EXE，按行解析 stdout JSON Stream。
 */
class ClipboardMonitor {
  public child: ChildProcess | null;

  constructor(
    private readonly exePath: string,
    private readonly args: string[] = []
  ) {
    this.child = null;
  }

  start(): void {
    console.log('正在启动 C# 原生剪贴板助手...');

    // 启动子进程，关闭独立窗口，直接接管 stdio 管道
    // stdin-pipe:  用于 stop() 时关闭 stdin，触发子进程优雅退出
    // stdout-pipe: 用于 readline 按行解析 JSON Stream
    // stderr-pipe: 用于透传 C# 侧 [DIAG] 诊断日志
    this.child = spawn(this.exePath, this.args, {
      stdio: ['pipe', 'pipe', 'pipe'],
      windowsHide: true,
    });

    // 将 C# 侧的 stderr（诊断日志）实时转发到当前进程 stderr
    this.child.stderr?.on('data', (data: Buffer) => {
      process.stderr.write(data);
    });

    // 利用 readline 模块实现按行读取，规避 TCP/Pipe 拆包粘包问题
    const rl = readline.createInterface({
      input: this.child.stdout!,
      terminal: false,
    });

    rl.on('line', (line: string) => {
      try {
        const message = JSON.parse(line) as ClipboardMessage;
        this.handleMessage(message);
      } catch (err) {
        console.error('解析 C# 消息时出错:', err, '原数据:', line);
      }
    });

    this.child.on('exit', (code: number | null, signal: string | null) => {
      console.warn(`C# 助手意外退出，Code: ${code}, Signal: ${signal}`);
      // 可在此处加入指数退避重试 (Exponential Backoff) 的重启机制
    });
  }

  handleMessage(msg: ClipboardMessage): void {
    switch (msg.type) {
      case 'text':
        console.log('[Electron 捕获] 剪贴板文本:', msg.payload);
        break;

      case 'files':
        console.log('[Electron 捕获] 剪贴板文件列表:', msg.payload);
        break;

      case 'image_path':
        console.log('[Electron 捕获] 临时图片路径:', msg.payload);
        this.processAndCleanImage(msg.payload);
        break;

      case 'error':
        console.error('[C# 内部错误]', msg.payload);
        break;
    }
  }

  async processAndCleanImage(tempFilePath: string): Promise<void> {
    try {
      // 确认文件确实存在
      if (fs.existsSync(tempFilePath)) {
        // 读取图片到内存（或移动到你想要长期保存的业务目录）
        const imageBuffer = fs.readFileSync(tempFilePath);
        console.log(`[处理中] 临时图片读取完毕，大小为: ${imageBuffer.length} 字节`);

        // 【关键步骤】消费完毕后，必须及时清理临时文件，防止用户长期运行导致磁盘膨胀
        await new Promise<void>((resolve, reject) => {
          fs.unlink(tempFilePath, (err: NodeJS.ErrnoException | null) => {
            if (err) {
              console.error('清理临时图片失败:', err);
              reject(err);
            } else {
              console.log('临时图片已成功清理，无磁盘滞留：', tempFilePath);
              resolve();
            }
          });
        });
      }
    } catch (e) {
      console.error('处理临时图片时出错:', e);
    }
  }

  stop(): void {
    if (this.child) {
      // 简单关闭子进程的 stdin。C# 端的 MonitorStdin 线程检测到后会自行优雅安全退出。
      this.child.stdin?.end();
      this.child = null;
    }
  }
}

export default ClipboardMonitor;
