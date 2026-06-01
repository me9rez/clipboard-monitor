using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace ClipboardMonitor.Tests;

public class MessageSerializationTests
{
    [Fact]
    public void SendJson_TextMessage_SerializesCorrectly()
    {
        var sw = new StringWriter();
        Console.SetOut(sw);

        Program.SendJson(new { type = "text", payload = "hello world" });

        var output = sw.ToString().Trim();
        var doc = JsonDocument.Parse(output);
        Assert.Equal("text", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("hello world", doc.RootElement.GetProperty("payload").GetString());
    }

    [Fact]
    public void SendJson_FilesMessage_SerializesCorrectly()
    {
        var sw = new StringWriter();
        Console.SetOut(sw);

        Program.SendJson(new { type = "files", payload = new[] { @"C:\file1.txt", @"C:\file2.txt" } });

        var output = sw.ToString().Trim();
        var doc = JsonDocument.Parse(output);
        Assert.Equal("files", doc.RootElement.GetProperty("type").GetString());
        var files = doc.RootElement.GetProperty("payload");
        Assert.Equal(2, files.GetArrayLength());
        Assert.Equal(@"C:\file1.txt", files[0].GetString());
        Assert.Equal(@"C:\file2.txt", files[1].GetString());
    }

    [Fact]
    public void SendJson_ImagePathMessage_SerializesCorrectly()
    {
        var sw = new StringWriter();
        Console.SetOut(sw);

        Program.SendJson(new { type = "image_path", payload = @"C:\temp\clip.png" });

        var output = sw.ToString().Trim();
        var doc = JsonDocument.Parse(output);
        Assert.Equal("image_path", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal(@"C:\temp\clip.png", doc.RootElement.GetProperty("payload").GetString());
    }

    [Fact]
    public void SendJson_ErrorMessage_SerializesCorrectly()
    {
        var sw = new StringWriter();
        Console.SetOut(sw);

        Program.SendJson(new { type = "error", payload = "something went wrong" });

        var output = sw.ToString().Trim();
        var doc = JsonDocument.Parse(output);
        Assert.Equal("error", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("something went wrong", doc.RootElement.GetProperty("payload").GetString());
    }

    [Fact]
    public void SendJson_UnicodeContent_PreservesCharacters()
    {
        var sw = new StringWriter();
        Console.SetOut(sw);

        Program.SendJson(new { type = "text", payload = "中文测试 🚀 ñoño" });

        var output = sw.ToString().Trim();
        var doc = JsonDocument.Parse(output);
        Assert.Equal("中文测试 🚀 ñoño", doc.RootElement.GetProperty("payload").GetString());
    }
}
