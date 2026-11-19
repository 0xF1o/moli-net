using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Moli.Net;

public static class MoliInstaller
{
    public const string DefaultVersion = "1.1.26";

    public static async Task<string> InstallAsync(string version = DefaultVersion, CancellationToken cancellationToken = default)
    {
        var windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var linux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        if (!windows && !linux) throw new PlatformNotSupportedException("Automatic Moli installation supports Windows and Linux. Supply ExecutablePath on other platforms.");
        if (!System.Version.TryParse(version, out var parsed) || parsed.Build < 0 || parsed.Revision >= 0)
            throw new ArgumentException("Use a three-part Moli release version, for example 1.1.26.", nameof(version));
        var architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x86_64",
            Architecture.Arm64 => "aarch64",
            _ => throw new PlatformNotSupportedException("Moli requires an x64 or ARM64 operating system.")
        };
        var executableName = windows ? "moli.exe" : "moli";
        var target = windows ? "pc-windows-msvc.zip" : "unknown-linux-gnu.tar.gz";
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Moli.Net");
        var directory = Path.Combine(root, $"{parsed}-{architecture}{(linux ? "-linux" : "")}");
        Directory.CreateDirectory(root);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var download = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        var token = download.Token;
        using var installLock = await AcquireLockAsync(directory + ".lock", token);
        if (Directory.Exists(directory)) return FindExecutable(directory, executableName);

        var staging = Path.Combine(root, $".install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Moli.Net/1.0");
            using var metadata = await client.GetAsync($"https://api.github.com/repos/lexmount/moli/releases/tags/v{parsed}", HttpCompletionOption.ResponseHeadersRead, token);
            metadata.EnsureSuccessStatusCode();
            using var metadataStream = await metadata.Content.ReadAsStreamAsync();
            var release = await JsonSerializer.DeserializeAsync<Release>(metadataStream, cancellationToken: token)
                ?? throw new InvalidDataException("GitHub returned an empty Moli release.");
            var name = $"moli-{architecture}-{target}";
            var asset = release.Assets.SingleOrDefault(asset => asset.Name == name)
                ?? throw new InvalidDataException($"Moli {parsed} has no asset named {name}.");
            if (asset.Digest is null || !asset.Digest.StartsWith("sha256:", StringComparison.Ordinal))
                throw new InvalidDataException("The Moli release asset has no SHA-256 digest.");
            var uri = new Uri(asset.DownloadUrl);
            if (uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com")
                throw new InvalidDataException("The Moli release download URL must use HTTPS on github.com.");
            var archive = Path.Combine(staging, name);
            using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                using var file = File.Create(archive);
                using var content = await response.Content.ReadAsStreamAsync();
                await content.CopyToAsync(file, 81920, token);
            }
            using (var file = File.OpenRead(archive))
            using (var sha256 = SHA256.Create())
            {
                token.ThrowIfCancellationRequested();
                var hash = BitConverter.ToString(sha256.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(asset.Digest, $"sha256:{hash}", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded Moli archive does not match the release SHA-256 digest.");
            }
            var extracted = Path.Combine(staging, "extracted");
            await MoliArchive.ExtractAsync(archive, extracted, windows, token);
            var executable = FindExecutable(extracted, executableName);
            if (linux) MoliArchive.MakeExecutable(executable);
            var relativeExecutable = executable.Substring(extracted.Length + 1);
            token.ThrowIfCancellationRequested();
            Directory.Move(extracted, directory);
            return Path.Combine(directory, relativeExecutable);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    private static string FindExecutable(string directory, string executableName) =>
        Directory.EnumerateFiles(directory, executableName, SearchOption.AllDirectories).SingleOrDefault()
            ?? throw new InvalidDataException($"The Moli installation in '{directory}' has no {executableName}. Remove that version's cache directory and retry.");

    private static async Task<FileStream> AcquireLockAsync(string path, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33 || (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && (ex.HResult & 0xFFFF) == 11))
            {
                await Task.Delay(100, cancellationToken);
            }
        }
    }

    private sealed record Release([property: JsonPropertyName("assets")] Asset[] Assets);
    private sealed record Asset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string DownloadUrl,
        [property: JsonPropertyName("digest")] string? Digest);
}
