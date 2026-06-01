using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;

class Program
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const uint CF_UNICODETEXT = 13;
    private const uint CF_HDROP = 15;
    private const uint CF_DIB = 8;
    private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

    // --- Win32 消息泵与窗口注册定义 ---
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent,
        IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    // --- 剪贴板底层 API 导入 ---
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    // --- 解析文件路径列表 (CF_HDROP) 所需原生 API ---
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder? lpszFile, uint cch);

    // --- 原生 GDI+ 导出（用于无损从内存 DIB 保存为 PNG 临时文件） ---
    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdiplusStartup(out IntPtr token, ref GdiplusStartupInput input, out GdiplusStartupOutput output);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdiplusShutdown(IntPtr token);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipCreateBitmapFromGdiDib(IntPtr bminfo, IntPtr pixdat, out IntPtr bitmap);

    [DllImport("gdiplus.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int GdipSaveImageToFile(IntPtr image, string filename, ref Guid clsidEncoder, IntPtr encoderParams);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipDisposeImage(IntPtr image);

    private struct GdiplusStartupInput
    {
        public uint GdiplusVersion;
        public IntPtr DebugEventCallback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
        public static GdiplusStartupInput Default() => new GdiplusStartupInput { GdiplusVersion = 1 };
    }

    private struct GdiplusStartupOutput
    {
        public IntPtr NotificationHook;
        public IntPtr NotificationUnhook;
    }

    private static WndProcDelegate? _wndProc;
    private static IntPtr _gdiplusToken;

    // --- 诊断日志 ---
    private static void LogDiag(string message)
    {
        Console.Error.WriteLine($"[DIAG] {message}");
        Console.Error.Flush();
    }

    static void Main(string[] args)
    {
        // 设置 stdout 强制以无缓冲 UTF-8 输出，规避中文及特殊字符乱码问题
        Console.OutputEncoding = Encoding.UTF8;
        LogDiag("进程已启动");

        // 初始化 GDI+ 用于后续图片处理
        var input = GdiplusStartupInput.Default();
        int gdiStatus = GdiplusStartup(out _gdiplusToken, ref input, out _);
        LogDiag($"GdiplusStartup 状态码: {gdiStatus}");

        // 绑定生命周期退出事件，注销监听并关闭 GDI+
        AppDomain.CurrentDomain.ProcessExit += (s, e) => { Cleanup(); };

        // 启动后台线程监听标准输入 (stdin)，若父进程退出，本进程将自杀
        Thread stdinMonitor = new Thread(MonitorStdin) { IsBackground = true };
        stdinMonitor.Start();
        LogDiag("stdin 监控线程已启动");

        _wndProc = WndProc;

        // .NET Core/.NET 5+ 中 Marshal.GetHINSTANCE 已废弃，返回 -1。
        // 使用 GetModuleHandle(null) 获取当前进程模块句柄。
        IntPtr hInstance = GetModuleHandle(null);
        LogDiag($"GetModuleHandle(null) = 0x{hInstance.ToInt64():X}");

        WNDCLASSEX wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            hInstance = hInstance,
            lpszClassName = "CoreClipboardListenerClass"
        };

        ushort atom = RegisterClassEx(ref wc);
        if (atom == 0)
        {
            int err = Marshal.GetLastWin32Error();
            LogDiag($"RegisterClassEx 失败，Win32 错误码: {err} (0x{err:X})");
            return;
        }
        LogDiag($"RegisterClassEx 成功，atom = 0x{atom:X}");

        IntPtr hwnd = CreateWindowEx(
            0, wc.lpszClassName, "CoreClipboardListenerWindow",
            0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, hInstance, IntPtr.Zero);

        if (hwnd == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            LogDiag($"CreateWindowEx 失败，Win32 错误码: {err} (0x{err:X})");
            return;
        }
        LogDiag($"CreateWindowEx 成功，hwnd = 0x{hwnd.ToInt64():X}");

        bool added = AddClipboardFormatListener(hwnd);
        if (!added)
        {
            int err = Marshal.GetLastWin32Error();
            LogDiag($"AddClipboardFormatListener 失败，Win32 错误码: {err} (0x{err:X})");
            return;
        }
        LogDiag("AddClipboardFormatListener 成功，剪贴板监听器已注册");

        // 启动 Win32 标准消息循环
        MSG msg;
        int result;
        LogDiag("进入消息循环...");
        while ((result = GetMessage(out msg, IntPtr.Zero, 0, 0)) != 0)
        {
            if (result == -1)
            {
                int err = Marshal.GetLastWin32Error();
                LogDiag($"GetMessage 返回 -1，Win32 错误码: {err} (0x{err:X})，消息循环异常退出");
                break;
            }
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        LogDiag($"消息循环已退出，GetMessage 返回值: {result}");
    }

    private static void MonitorStdin()
    {
        // Console.ReadLine() 在父进程退出或标准输入流关闭时，会阻塞返回 null
        while (true)
        {
            string? line = Console.ReadLine();
            if (line == null)
            {
                LogDiag("stdin 已关闭，进程即将退出");
                // 父进程已死，立即退出本进程，避免残留孤儿进程
                Environment.Exit(0);
            }
        }
    }

    private static void Cleanup()
    {
        if (_gdiplusToken != IntPtr.Zero)
        {
            GdiplusShutdown(_gdiplusToken);
        }
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            LogDiag("WndProc 收到 WM_CLIPBOARDUPDATE");
            // 为避免频繁复制大文件时产生竞态锁冲突，添加 50ms 延迟，等待复制源释放锁
            Thread.Sleep(50);
            ProcessClipboard();
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private static void ProcessClipboard()
    {
        LogDiag("ProcessClipboard 开始处理...");
        if (!OpenClipboard(IntPtr.Zero))
        {
            int err = Marshal.GetLastWin32Error();
            LogDiag($"OpenClipboard 失败，Win32 错误码: {err} (0x{err:X})");
            return;
        }

        try
        {
            // 1. 处理文件列表格式 (CF_HDROP)
            if (IsClipboardFormatAvailable(CF_HDROP))
            {
                LogDiag("检测到 CF_HDROP 格式");
                IntPtr hDrop = GetClipboardData(CF_HDROP);
                if (hDrop != IntPtr.Zero)
                {
                    uint fileCount = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
                    string[] paths = new string[fileCount];
                    for (uint i = 0; i < fileCount; i++)
                    {
                        uint pathLen = DragQueryFile(hDrop, i, null, 0);
                        StringBuilder sb = new StringBuilder((int)pathLen + 1);
                        DragQueryFile(hDrop, i, sb, (uint)sb.Capacity);
                        paths[i] = sb.ToString();
                    }

                    SendJson(new { type = "files", payload = paths });
                    return; // 拦截，文件优先于图片及文本
                }
            }

            // 2. 处理图片格式 (CF_DIB)
            if (IsClipboardFormatAvailable(CF_DIB))
            {
                LogDiag("检测到 CF_DIB 格式");
                IntPtr hDib = GetClipboardData(CF_DIB);
                if (hDib != IntPtr.Zero)
                {
                    IntPtr pDib = GlobalLock(hDib);
                    if (pDib != IntPtr.Zero)
                    {
                        try
                        {
                            string tempFile = SaveDibToTempPng(pDib);
                            if (!string.IsNullOrEmpty(tempFile))
                            {
                                SendJson(new { type = "image_path", payload = tempFile });
                                return;
                            }
                        }
                        finally
                        {
                            GlobalUnlock(hDib);
                        }
                    }
                }
            }

            // 3. 处理文本格式 (CF_UNICODETEXT)
            if (IsClipboardFormatAvailable(CF_UNICODETEXT))
            {
                LogDiag("检测到 CF_UNICODETEXT 格式");
                IntPtr hGlobal = GetClipboardData(CF_UNICODETEXT);
                if (hGlobal != IntPtr.Zero)
                {
                    IntPtr lpstr = GlobalLock(hGlobal);
                    if (lpstr != IntPtr.Zero)
                    {
                        try
                        {
                            string? text = Marshal.PtrToStringUni(lpstr);
                            if (text != null)
                            {
                                SendJson(new { type = "text", payload = text });
                            }
                        }
                        finally
                        {
                            GlobalUnlock(hGlobal);
                        }
                    }
                }
            }
            else
            {
                LogDiag("未检测到已支持的剪贴板格式 (CF_HDROP/CF_DIB/CF_UNICODETEXT)");
            }
        }
        catch (Exception ex)
        {
            // 将内部错误包装为 JSON 输出至前端，方便排查
            LogDiag($"ProcessClipboard 异常: {ex.Message}");
            SendJson(new { type = "error", payload = ex.Message });
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static string SaveDibToTempPng(IntPtr pDib)
    {
        try
        {
            // 解析 DIB（设备无关位图）的 BITMAPINFOHEADER 结构
            int bmiSize = Marshal.ReadInt32(pDib);
            int width = Marshal.ReadInt32(pDib, 4);
            int height = Marshal.ReadInt32(pDib, 8);
            ushort planes = (ushort)Marshal.ReadInt16(pDib, 12);
            ushort bitCount = (ushort)Marshal.ReadInt16(pDib, 14);
            int compression = Marshal.ReadInt32(pDib, 16);
            int imageSize = Marshal.ReadInt32(pDib, 20);

            // 颜色表及调色板偏移计算
            int colorTableSize = 0;
            if (bitCount <= 8)
            {
                int clrUsed = Marshal.ReadInt32(pDib, 32);
                colorTableSize = (clrUsed > 0 ? clrUsed : (1 << bitCount)) * 4;
            }
            else if (compression == 3) // BI_BITFIELDS
            {
                colorTableSize = 12; // 3 个 RGB 掩码，每个 4 字节
            }

            IntPtr pBmi = pDib;
            IntPtr pBits = new IntPtr(pDib.ToInt64() + bmiSize + colorTableSize);

            // 调用 GDI+ 原生函数创建 Bitmap 对象
            int status = GdipCreateBitmapFromGdiDib(pBmi, pBits, out IntPtr hBitmap);
            if (status != 0 || hBitmap == IntPtr.Zero)
            {
                LogDiag($"GdipCreateBitmapFromGdiDib 失败，状态码: {status}");
                return string.Empty;
            }

            try
            {
                // 生成临时文件路径
                string tempDir = Path.GetTempPath();
                string fileName = $"clip_temp_{DateTime.Now.Ticks}_{Guid.NewGuid().ToString("N").Substring(0, 8)}.png";
                string fullPath = Path.Combine(tempDir, fileName);

                // PNG 编码器的 CLSID (固定值)
                Guid pngClsid = new Guid("557cf406-1a04-11d3-9a73-0000f81ef32e");

                // 保存为 PNG 图片文件
                status = GdipSaveImageToFile(hBitmap, fullPath, ref pngClsid, IntPtr.Zero);
                if (status == 0)
                {
                    LogDiag($"图片已保存到临时文件: {fullPath}");
                    return fullPath;
                }
                LogDiag($"GdipSaveImageToFile 失败，状态码: {status}");
            }
            finally
            {
                GdipDisposeImage(hBitmap);
            }
        }
        catch (Exception ex)
        {
            LogDiag($"SaveDibToTempPng 异常: {ex.Message}");
        }
        return string.Empty;
    }

    internal static void SendJson(object data)
    {
        try
        {
            // 序列化后单行输出，并强制刷新 stdout 缓存，保证极低的延迟
            string json = JsonSerializer.Serialize(data);
            Console.WriteLine(json);
            Console.Out.Flush();
        }
        catch { }
    }
}
