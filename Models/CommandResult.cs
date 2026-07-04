namespace PCToMobile.Models;

public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;

    public string BestMessage =>
        !string.IsNullOrWhiteSpace(StandardOutput)
            ? StandardOutput.Trim()
            : StandardError.Trim();
}
