using System.Reflection;
using System.Windows;
using CapyLulu;

var tests = new (string Name, Action Run)[]
{
    ("gaze direction wraps clockwise", TestGazeClockwiseWrap),
    ("gaze direction chooses shortest path", TestGazeShortestPath),
    ("circular angle distance wraps", TestCircularDistance),
    ("pointer tracker detects horizontal flick", TestHorizontalFlick),
    ("pointer tracker detects lift and drop", TestLiftDrop),
    ("active focus session does not restart", TestFocusSession),
    ("dialogue resource contains required groups", TestDialogueCatalog),
    ("character manifests expose stable unique ids", TestCharacterCatalog),
    ("all shipped sprite sheets match their manifests", TestSpriteSheets)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {test.Name}: {exception.Message}");
    }
}

foreach (var failure in failures)
{
    Console.Error.WriteLine(failure);
}

if (failures.Count > 0)
{
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine($"Validated {tests.Length} scenarios.");

static void TestGazeClockwiseWrap() => Equal(0, GazeDirectionMath.StepToward(15, 1));

static void TestGazeShortestPath() => Equal(15, GazeDirectionMath.StepToward(0, 14));

static void TestCircularDistance() => Near(2, GazeDirectionMath.CircularAngleDistance(359, 1));

static void TestHorizontalFlick()
{
    var tracker = new PointerMotionTracker();
    tracker.Reset(new Point(0, 0), 0);
    tracker.Add(new Point(45, 2), 0.04);
    tracker.Add(new Point(110, 3), 0.08);
    Equal(PetGesture.HorizontalFlick, tracker.DetectGesture());
}

static void TestLiftDrop()
{
    var tracker = new PointerMotionTracker();
    tracker.Reset(new Point(0, 120), 0);
    tracker.Add(new Point(2, 10), 0.25);
    tracker.Add(new Point(3, 105), 0.55);
    Equal(PetGesture.LiftDrop, tracker.DetectGesture());
}

static void TestFocusSession()
{
    var now = DateTimeOffset.Parse("2026-08-31T00:00:00Z");
    var session = new FocusSession();
    Equal(TimeSpan.FromMinutes(10), session.Start(now, TimeSpan.FromMinutes(10)));
    Equal(TimeSpan.FromMinutes(9), session.Start(now.AddMinutes(1), TimeSpan.FromMinutes(10)));
    Equal(TimeSpan.Zero, session.GetRemaining(now.AddMinutes(11)));
}

static void TestDialogueCatalog()
{
    var catalog = PetDialogueCatalog.Load(typeof(PetDialogueCatalog).Assembly);
    True(catalog.BubbleMessages.Length >= 20, "陪伴文案数量不足");
    True(catalog.SingingLyrics.Length >= 6, "歌词数量不足");
}

static void TestCharacterCatalog()
{
    var characters = new CharacterCatalog(typeof(CharacterCatalog).Assembly).Discover();
    True(characters.Count == 4, $"预期 4 个角色，实际 {characters.Count} 个");
    True(characters.Select(character => character.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == characters.Count,
        "角色 ID 不唯一");
    True(characters.Count(character => character.IsLoafing) == 1, "摸鱼角色必须且只能有一个");
    True(characters.All(character => !string.IsNullOrWhiteSpace(character.DisplayName)), "角色显示名不能为空");
}

static void TestSpriteSheets()
{
    var catalog = new CharacterCatalog(typeof(CharacterCatalog).Assembly);
    foreach (var character in catalog.Discover())
    {
        var sheet = catalog.LoadSprite(character);
        True(sheet.Rows > 0, $"{character.Id} 没有动作行");
        True(sheet.GetFrameCount(0) > 0, $"{character.Id} 没有待机帧");
        True(sheet.GetPlayableClickRows().Count > 0, $"{character.Id} 没有互动帧");
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"expected {expected}, actual {actual}");
    }
}

static void Near(double expected, double actual)
{
    if (Math.Abs(expected - actual) > 0.001)
    {
        throw new InvalidOperationException($"expected {expected}, actual {actual}");
    }
}

static void True(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
