namespace CapyLulu;

internal static class PetBehaviorOptions
{
    public const double MinimumScale = 0.50;
    public const double MaximumScale = 1.00;
    public const double ScaleStep = 0.25;
    public const double DragThreshold = 5.0;
    public const double DragDirectionEnterSpeed = 70.0;
    public const double DragDirectionSwitchSpeed = 135.0;
    public const double LiftDurationSeconds = 0.14;
    public const double DropDurationSeconds = 0.30;
    public const double GazeDeadZone = 90.0;
    public const double GazeMaximumDistance = 460.0;
    public const double GazeSectorDegrees = 22.5;
    public const double GazeHysteresisDegrees = 5.0;
    public const double GazeSampleIntervalSeconds = 0.045;
    public const double GazeActivationSpeed = 250.0;
    public const double GazeDirectionDwellSeconds = 0.20;
    public const double GazeStepIntervalSeconds = 0.12;
    public const double GazeExitDelaySeconds = 0.30;
    public static readonly TimeSpan FocusDuration = TimeSpan.FromMinutes(10);
}
