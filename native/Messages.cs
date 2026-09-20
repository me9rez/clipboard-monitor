using System.Text.Json.Serialization;

/// <summary>
/// 字符串负载消息（text / image_path / error 三类共用）。
/// 属性名即协议字段名（type / payload），不可改名。
/// </summary>
internal sealed record StringPayloadMessage(string type, string payload)
{
    public static StringPayloadMessage Text(string text) => new("text", text);

    public static StringPayloadMessage ImagePath(string path) => new("image_path", path);

    public static StringPayloadMessage Error(string message) => new("error", message);
}

/// <summary>
/// 文件列表消息（files）。属性名即协议字段名（type / payload），不可改名。
/// </summary>
internal sealed record FileListMessage(string type, string[] payload)
{
    public static FileListMessage Files(string[] paths) => new("files", paths);
}

/// <summary>
/// System.Text.Json 源生成上下文。
///
/// Native AOT 下反射式序列化被禁用（JsonSerializer.IsReflectionEnabledByDefault = false），
/// 旧写法 JsonSerializer.Serialize(匿名对象) 会在运行时抛 InvalidOperationException；
/// 若改用 -p:JsonSerializerIsReflectionEnabledByDefault=true 兜底，匿名类型属性会被裁剪，
/// 序列化结果静默变成 "{}"。因此发送消息必须走本上下文（或新增 [JsonSerializable] 条目）。
/// </summary>
[JsonSerializable(typeof(StringPayloadMessage))]
[JsonSerializable(typeof(FileListMessage))]
internal partial class ClipboardJsonContext : JsonSerializerContext
{
}
