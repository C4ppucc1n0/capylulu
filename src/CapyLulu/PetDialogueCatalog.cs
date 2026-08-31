using System.IO;
using System.Reflection;
using System.Text.Json;

namespace CapyLulu;

internal sealed class PetDialogueCatalog
{
    private const string ResourceName = "CapyLulu.Resources.dialogues.zh-CN.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string[] SingingLyrics { get; init; } = [];
    public string[] BubbleMessages { get; init; } = [];
    public string[] DragBubbleMessages { get; init; } = [];
    public string[] LoafingBubbleMessages { get; init; } = [];
    public string[] LoafingDragBubbleMessages { get; init; } = [];
    public string[] LoafingIdleMessages { get; init; } = [];
    public Dictionary<string, string[]> GestureMessages { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string[]> LoafingGestureMessages { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string[] HappyMessages { get; init; } = [];
    public string[] SleepyMessages { get; init; } = [];
    public string[] WorkingMessages { get; init; } = [];

    public IReadOnlyList<string> GetGestureMessages(PetGesture gesture, bool loafing)
    {
        var source = loafing ? LoafingGestureMessages : GestureMessages;
        return source.TryGetValue(gesture.ToString(), out var messages) ? messages : [];
    }

    public static PetDialogueCatalog Load(Assembly? assembly = null)
    {
        assembly ??= Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException($"缺少内嵌文案资源：{ResourceName}");
        var catalog = JsonSerializer.Deserialize<PetDialogueCatalog>(stream, JsonOptions)
            ?? throw new InvalidDataException("文案资源内容为空。");
        catalog.Validate();
        return catalog;
    }

    private void Validate()
    {
        if (SingingLyrics.Length == 0 || BubbleMessages.Length == 0
            || HappyMessages.Length == 0 || SleepyMessages.Length == 0
            || WorkingMessages.Length == 0)
        {
            throw new InvalidDataException("文案资源缺少必要分组。");
        }
    }
}
