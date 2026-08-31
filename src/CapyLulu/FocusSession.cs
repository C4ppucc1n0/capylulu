namespace CapyLulu;

internal sealed class FocusSession
{
    public DateTimeOffset? EndsAt { get; private set; }

    public bool IsActive(DateTimeOffset now) => EndsAt is { } end && end > now;

    public TimeSpan Start(DateTimeOffset now, TimeSpan duration)
    {
        if (!IsActive(now))
        {
            EndsAt = now + duration;
        }

        return GetRemaining(now);
    }

    public TimeSpan GetRemaining(DateTimeOffset now)
    {
        if (EndsAt is not { } end)
        {
            return TimeSpan.Zero;
        }

        var remaining = end - now;
        if (remaining > TimeSpan.Zero)
        {
            return remaining;
        }

        EndsAt = null;
        return TimeSpan.Zero;
    }
}
