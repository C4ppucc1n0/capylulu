using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace CapyLulu;

internal enum PetAction
{
    Idle,
    DragRight,
    DragLeft,
    Click,
    Lift,
    Drop,
    Waiting,
    Working,
    Review,
    GestureFlick,
    GestureShake,
    GestureLiftDrop
}

internal enum PetInteractionState
{
    Idle,
    ClickAction,
    Lifting,
    DraggingNeutral,
    DraggingLeft,
    DraggingRight,
    Dropping,
    GestureReaction,
    Looking
}

internal enum PetGesture
{
    None,
    HorizontalFlick,
    Shake,
    LiftDrop
}

internal sealed class PetActionManifest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public int SpriteVersionNumber { get; set; } = 1;

    public Dictionary<string, int> Actions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int[] ClickRows { get; set; } = [];

    public int[] LookRows { get; set; } = [];

    public bool HasLookDirections => SpriteVersionNumber >= 2 && LookRows.Length == 2;

    public int? GetRow(PetAction action, int rowCount)
    {
        var key = action switch
        {
            PetAction.DragRight => "dragRight",
            PetAction.DragLeft => "dragLeft",
            PetAction.GestureFlick => "gestureFlick",
            PetAction.GestureShake => "gestureShake",
            PetAction.GestureLiftDrop => "gestureLiftDrop",
            _ => char.ToLowerInvariant(action.ToString()[0]) + action.ToString()[1..]
        };

        return Actions.TryGetValue(key, out var row) && row >= 0 && row < rowCount
            ? row
            : null;
    }

    public IReadOnlyList<int> GetClickRows(int rowCount)
    {
        var validRows = ClickRows.Where(row => row > 0 && row < rowCount).Distinct().ToArray();
        if (validRows.Length > 0)
        {
            return validRows;
        }

        return Enumerable.Range(1, Math.Max(0, rowCount - 1)).ToArray();
    }

    public static PetActionManifest? LoadForResource(Assembly assembly, string spriteResourceName)
    {
        const string resourcePrefix = "CapyLulu.GeneratedActions.";
        var fileName = spriteResourceName.StartsWith(resourcePrefix, StringComparison.Ordinal)
            ? spriteResourceName[resourcePrefix.Length..]
            : spriteResourceName;
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var manifestResourceName = $"CapyLulu.GeneratedActions.{baseName}.pet.json";
        using var stream = assembly.GetManifestResourceStream(manifestResourceName);
        if (stream is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PetActionManifest>(stream, JsonOptions)
                ?? null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static PetActionManifest CreateV2Default()
    {
        return new PetActionManifest
        {
            SpriteVersionNumber = 2,
            Actions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["idle"] = 0,
                ["dragRight"] = 1,
                ["dragLeft"] = 2,
                ["click"] = 3,
                ["lift"] = 4,
                ["drop"] = 4,
                ["gestureFlick"] = 5,
                ["gestureShake"] = 5,
                ["waiting"] = 6,
                ["working"] = 7,
                ["review"] = 8,
                ["gestureLiftDrop"] = 4
            },
            ClickRows = [3, 4, 5, 6, 7, 8],
            LookRows = [9, 10]
        };
    }
}

internal sealed class PointerMotionTracker
{
    // Keep enough history for the deliberate lift-and-drop gesture; velocity queries
    // still use only their short trailing window.
    private const double SampleWindowSeconds = 1.60;
    private readonly List<MotionSample> _samples = [];

    public Point StartPosition => _samples.Count > 0 ? _samples[0].Position : default;

    public Point CurrentPosition => _samples.Count > 0 ? _samples[^1].Position : default;

    public double DurationSeconds => _samples.Count > 1 ? _samples[^1].Time - _samples[0].Time : 0;

    public void Reset(Point position, double time)
    {
        _samples.Clear();
        _samples.Add(new MotionSample(position, time));
    }

    public void Add(Point position, double time)
    {
        if (_samples.Count > 0 && time <= _samples[^1].Time)
        {
            return;
        }

        _samples.Add(new MotionSample(position, time));
        var cutoff = time - SampleWindowSeconds;
        while (_samples.Count > 2 && _samples[1].Time < cutoff)
        {
            _samples.RemoveAt(0);
        }
    }

    public Vector GetVelocity(double windowSeconds = 0.12)
    {
        if (_samples.Count < 2)
        {
            return default;
        }

        var newest = _samples[^1];
        var oldest = _samples[0];
        for (var index = _samples.Count - 2; index >= 0; index--)
        {
            oldest = _samples[index];
            if (newest.Time - oldest.Time >= windowSeconds)
            {
                break;
            }
        }

        var duration = newest.Time - oldest.Time;
        return duration > 0.001 ? (newest.Position - oldest.Position) / duration : default;
    }

    public PetGesture DetectGesture()
    {
        if (_samples.Count < 3)
        {
            return PetGesture.None;
        }

        var start = StartPosition;
        var end = CurrentPosition;
        var minY = _samples.Min(sample => sample.Position.Y);
        var lifted = start.Y - minY;
        var dropped = end.Y - minY;
        if (DurationSeconds is >= 0.20 and <= 1.50 && lifted >= 90 && dropped >= 70)
        {
            return PetGesture.LiftDrop;
        }

        var reversals = 0;
        var lastDirection = 0;
        var accumulated = 0.0;
        var horizontalPath = 0.0;
        var verticalPath = 0.0;
        for (var index = 1; index < _samples.Count; index++)
        {
            var delta = _samples[index].Position - _samples[index - 1].Position;
            horizontalPath += Math.Abs(delta.X);
            verticalPath += Math.Abs(delta.Y);
            var direction = Math.Abs(delta.X) >= 2 ? Math.Sign(delta.X) : 0;
            if (direction == 0)
            {
                continue;
            }

            if (lastDirection == 0 || direction == lastDirection)
            {
                accumulated += Math.Abs(delta.X);
                lastDirection = direction;
                continue;
            }

            if (accumulated >= 28)
            {
                reversals++;
            }

            accumulated = Math.Abs(delta.X);
            lastDirection = direction;
        }

        if (reversals >= 3 && horizontalPath >= 220 && horizontalPath >= verticalPath * 1.6)
        {
            return PetGesture.Shake;
        }

        var velocity = GetVelocity();
        if (Math.Abs(velocity.X) >= 1050 && Math.Abs(velocity.X) >= Math.Abs(velocity.Y) * 1.7)
        {
            return PetGesture.HorizontalFlick;
        }

        return PetGesture.None;
    }

    private readonly record struct MotionSample(Point Position, double Time);
}
