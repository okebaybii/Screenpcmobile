namespace PCToMobile.Models;

public sealed class MirrorOptions
{
    public int MaxSize { get; init; } = 1920;
    public int MaxFps { get; init; } = 60;
    public int BitRateMbps { get; init; } = 8;
    public bool ForwardAudio { get; init; } = true;
    public bool StayAwake { get; init; } = true;
    public bool TurnScreenOff { get; init; }
    public string? RecordPath { get; init; }
}
