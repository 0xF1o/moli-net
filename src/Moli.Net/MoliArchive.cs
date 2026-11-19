using System.ComponentModel;
using System.IO.Compression;
using System.Runtime.InteropServices;
#if NETSTANDARD2_0
using System.Text;
using ICSharpCode.SharpZipLib.Tar;
#else
using System.Formats.Tar;
#endif

namespace Moli.Net;

internal static class MoliArchive
{
    internal static async Task ExtractAsync(string archive, string directory, bool zip, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        if (zip)
        {
            using var entries = ZipFile.OpenRead(archive);
            foreach (var entry in entries.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = ResolveDestination(directory, entry.FullName);
                if (entry.Name.Length == 0)
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }
                using var source = entry.Open();
                await WriteFileAsync(source, destination, cancellationToken);
            }
            return;
        }

        using var file = File.OpenRead(archive);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
#if NETSTANDARD2_0
        using var tar = new TarInputStream(gzip, Encoding.UTF8);
        while (await tar.GetNextEntryAsync(cancellationToken) is { } entry)
        {
            var destination = ResolveDestination(directory, entry.Name);
            if (entry.IsDirectory)
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            if (entry.TarHeader.TypeFlag is not (TarHeader.LF_NORMAL or TarHeader.LF_OLDNORM))
                throw new InvalidDataException($"Unsupported TAR entry type for '{entry.Name}'.");
            await WriteFileAsync(tar, destination, cancellationToken);
        }
#else
        await TarFile.ExtractToDirectoryAsync(gzip, directory, overwriteFiles: false, cancellationToken);
#endif
    }

    private static string ResolveDestination(string directory, string entry)
    {
        var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(root, entry));
        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!destination.StartsWith(root, comparison))
            throw new InvalidDataException($"Archive entry '{entry}' escapes the installation directory.");
        return destination;
    }

    private static async Task WriteFileAsync(Stream source, string destination, CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(destination) ?? throw new InvalidDataException($"Archive entry '{destination}' has no parent directory.");
        Directory.CreateDirectory(parent);
        using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(target, 81920, cancellationToken);
    }

    internal static void MakeExecutable(string executable)
    {
#if NETSTANDARD2_0
        if (Chmod(executable, 493) != 0) throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to make '{executable}' executable.");
#else
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux executable permissions require Linux.");
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
#endif
    }

#if NETSTANDARD2_0
    [DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
    private static extern int Chmod(string path, uint mode);
#endif
}
