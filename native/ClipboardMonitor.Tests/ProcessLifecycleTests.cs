using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace ClipboardMonitor.Tests;

public class ProcessLifecycleTests : IDisposable
{
    private readonly string _nativeProjectDir;

    public ProcessLifecycleTests()
    {
        _nativeProjectDir = FindNativeProjectDirectory();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Process_Exits_When_Stdin_Closed()
    {
        string exePath = BuildAndGetExePath();
        Assert.True(File.Exists(exePath), $"EXE not found at {exePath}");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exePath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        Assert.False(process.HasExited);

        // Close stdin to trigger the MonitorStdin thread to exit
        process.StandardInput.Close();

        bool exited = process.WaitForExit(5000);
        Assert.True(exited, "Process did not exit within 5 seconds after stdin was closed");
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public void Process_Exits_When_Stdin_Pipe_Broken()
    {
        string exePath = BuildAndGetExePath();
        Assert.True(File.Exists(exePath), $"EXE not found at {exePath}");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exePath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        // Write a line then close - simulating parent process sending data then exiting
        process.StandardInput.WriteLine("ping");
        process.StandardInput.Close();

        bool exited = process.WaitForExit(5000);
        Assert.True(exited, "Process did not exit within 5 seconds after stdin pipe was broken");
    }

    private string BuildAndGetExePath()
    {
        string config = "Release";
        string exePath = Path.Combine(_nativeProjectDir, "bin", config, "net10.0-windows", "ClipboardMonitor.exe");

        if (!File.Exists(exePath))
        {
            var build = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"build \"{Path.Combine(_nativeProjectDir, "ClipboardMonitor.csproj")}\" -c {config}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            build.Start();
            build.WaitForExit();
            Assert.Equal(0, build.ExitCode);
        }

        return exePath;
    }

    private static string FindNativeProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.Name.Equals("native", StringComparison.OrdinalIgnoreCase))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        // Fallback: walk up from current directory looking for native folder relative to project root
        var fallback = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (fallback != null)
        {
            var nativeDir = new DirectoryInfo(Path.Combine(fallback.FullName, "native"));
            if (nativeDir.Exists)
            {
                return nativeDir.FullName;
            }
            fallback = fallback.Parent;
        }

        throw new InvalidOperationException("Could not locate 'native' project directory.");
    }
}
