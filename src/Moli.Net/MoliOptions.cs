namespace Moli.Net;

public sealed record MoliOptions
{
    public string? ExecutablePath { get; init; }
    public string Version { get; init; } = MoliInstaller.DefaultVersion;
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public bool EnableLayout { get; init; } = true;
    public bool LoadResources { get; init; }
}
