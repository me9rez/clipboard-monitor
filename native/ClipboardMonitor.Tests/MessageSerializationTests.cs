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

        Program.SendJson(StringPayloadMessage.Text("hello world"));

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

        Program.SendJson(FileListMessage.Files(new[] { @"C:\file1.txt", @"C:\file2.txt" }));

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

        Program.SendJson(StringPayloadMessage.ImagePath(@"C:\temp\clip.png"));

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

        Program.SendJson(StringPayloadMessage.Error("something went wrong"));

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

        Program.SendJson(StringPayloadMessage.Text("中文测试 🚀 ñoño"));

        var output = sw.ToString().Trim();
        var doc = JsonDocument.Parse(output);
        Assert.Equal("中文测试 🚀 ñoño", doc.RootElement.GetProperty("payload").GetString());
    }

    /// <summary>
    /// 协议字段名与顺序即对外契约（{ type, payload }），且 Native AOT 下必须由源生成上下文产出，
    /// 故直接比对完整序列化字符串，防止退化为反射式（AOT 下会抛异常）或输出 "{}"。
    /// </summary>
    [Fact]
    public void SendJson_MatchesExactProtocolShape()
    {
        var sw = new StringWriter();
        Console.SetOut(sw);

        Program.SendJson(StringPayloadMessage.Text("hello world"));

        Assert.Equal("{\"type\":\"text\",\"payload\":\"hello world\"}", sw.ToString().Trim());
    }

    /// <summary>
    /// Native AOT 下反射式序列化默认关闭；此处断言项目已具备 AOT 安全的源生成入口，
    /// 且指定类型的 JsonTypeInfo 可被解析（未注册类型会在 AOT 下静默产出 "{}"）。
    /// </summary>
    [Fact]
    public void JsonContext_IsUsableAndCoversProtocolTypes()
    {
        Assert.NotNull(ClipboardJsonContext.Default.StringPayloadMessage);
        Assert.NotNull(ClipboardJsonContext.Default.FileListMessage);

        string json = JsonSerializer.Serialize(
            FileListMessage.Files(new[] { @"C:\a.txt" }),
            ClipboardJsonContext.Default.FileListMessage);
        Assert.Equal("{\"type\":\"files\",\"payload\":[\"C:\\\\a.txt\"]}", json);
    }
}
