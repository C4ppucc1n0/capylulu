using System.IO;
using System.Reflection;

namespace CapyLulu;

internal sealed record CharacterDefinition(
    string Id,
    string DisplayName,
    string ResourceName,
    PetActionManifest Actions)
{
    public bool IsLoafing => Actions.HasRole("loafing");
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
        var manifest = PetActionManifest.LoadForResource(_assembly, resourceName)
            ?? new PetActionManifest();
        var fileName = resourceName[ResourcePrefix.Length..];
        var fallbackId = Path.GetFileNameWithoutExtension(fileName);
        return new CharacterDefinition(
            string.IsNullOrWhiteSpace(manifest.Id) ? fallbackId : manifest.Id,
            string.IsNullOrWhiteSpace(manifest.DisplayName) ? fallbackId : manifest.DisplayName,
            resourceName,
            manifest);
    }

    private static bool IsSpriteResource(string name) =>
        name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
        && (name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
}
