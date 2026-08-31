namespace CapyLulu;

internal static class GazeDirectionMath
{
    public static int StepToward(int current, int target)
    {
        var clockwiseSteps = (target - current + 16) % 16;
        if (clockwiseSteps == 0)
        {
            return current;
        }

        return clockwiseSteps <= 8
            ? (current + 1) % 16
            : (current + 15) % 16;
    }

    public static double CircularAngleDistance(double first, double second)
    {
        var difference = Math.Abs(first - second) % 360;
        return difference > 180 ? 360 - difference : difference;
    }
}
