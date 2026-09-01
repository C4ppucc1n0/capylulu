using System.IO;
using System.Reflection;

namespace CapyLulu;

// Actions 为 null 表示资源没有附带清单，由 SpriteSheet.Load 按实际行数决定默认动作表。
internal sealed record CharacterDefinition(
    string Id,
    string DisplayName,
    string ResourceName,
    PetActionManifest? Actions)
{
    public bool IsLoafing => Actions?.HasRole("loafing") ?? false;
}

internal sealed class CharacterCatalog
{
    private const string ResourcePrefix = "CapyLulu.GeneratedActions.";
    private readonly Assembly _assembly;

    public CharacterCatalog(Assembly? assembly = null)
    {
        _assembly = assembly ?? Assembly.GetExecutingAssembly();
    }

    public IReadOnlyList<CharacterDefinition> Discover()
    {
        var characters = _assembly
            .GetManifestResourceNames()
            .Where(IsSpriteResource)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(CreateDefinition)
            .ToArray();
        var duplicateId = characters
            .GroupBy(character => character.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateId is not null)
        {
            throw new InvalidDataException($"角色清单包含重复 ID：{duplicateId}");
        }

        return characters;
    }

    public SpriteSheet LoadSprite(CharacterDefinition character)
    {
        using var stream = _assembly.GetManifestResourceStream(character.ResourceName)
            ?? throw new InvalidDataException($"无法打开内嵌动作资源：{character.ResourceName}");
        return SpriteSheet.Load(stream, character.ResourceName, character.Actions);
    }

    private CharacterDefinition CreateDefinition(string resourceName)
    {
        // 这里不要用 new PetActionManifest() 兜底：那是 v1 清单，会让 v2 图集失去注视与帧数推断。
        var manifest = PetActionManifest.LoadForResource(_assembly, resourceName);
        var fileName = resourceName[ResourcePrefix.Length..];
        var fallbackId = Path.GetFileNameWithoutExtension(fileName);
        var id = manifest?.Id;
        var displayName = manifest?.DisplayName;
        return new CharacterDefinition(
            string.IsNullOrWhiteSpace(id) ? fallbackId : id,
            string.IsNullOrWhiteSpace(displayName) ? fallbackId : displayName,
            resourceName,
            manifest);
    }

    private static bool IsSpriteResource(string name) =>
        name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
        && (name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
}
