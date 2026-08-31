namespace CapyLulu;

internal sealed class PetSettings
{
    public int SchemaVersion { get; set; } = 2;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Scale { get; set; } = 1.0;
    public bool Topmost { get; set; } = true;
    public string? SelectedCharacterId { get; set; }

    // v1 compatibility: older releases persisted the full embedded resource name.
    public string? SelectedCharacter { get; set; }

    public bool LoafingMode { get; set; }
    public PetMood Mood { get; set; } = PetMood.Happy;
    public PetGazeMode GazeMode { get; set; } = PetGazeMode.Follow;
}

internal enum PetMood
{
    Happy,
    Sleepy,
    Working
}

internal enum PetGazeMode
{
    Quiet,
    Follow
}
