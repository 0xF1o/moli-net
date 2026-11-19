using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Moli.Net;

public sealed class MoliServer : IAsyncDisposable
{
    private readonly Process process;
    private readonly Task outputDrained;
    private Task? disposal;

    private MoliServer(Process process, Uri endpoint, Task outputDrained)
    {
        this.process = process;
        Endpoint = endpoint;
        this.outputDrained = outputDrained;
    }

    public Uri Endpoint { get; }
    public int ProcessId => process.Id;

    public static async Task<MoliServer> StartAsync(MoliOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        if (options.ExecutablePath is not null && string.IsNullOrWhiteSpace(options.ExecutablePath))
            throw new ArgumentException("ExecutablePath must not be empty or whitespace.", nameof(options));
        if (options.StartupTimeout <= TimeSpan.Zero || options.StartupTimeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(options), "StartupTimeout must be positive and no greater than Int32.MaxValue milliseconds.");

        cancellationToken.ThrowIfCancellationRequested();
        var executable = options.ExecutablePath ?? await MoliInstaller.InstallAsync(options.Version, cancellationToken);
        var (startInfo, endpoint) = CreateStartInfo(options, executable);
        var diagnostics = new StringBuilder();
        var process = new Process { StartInfo = startInfo };
        var outputCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closedStreams = 0;
        void CaptureOutput(object sender, DataReceivedEventArgs e)
        {
            if (e.Data is not null) AppendDiagnostic(diagnostics, e.Data);
            else if (Interlocked.Increment(ref closedStreams) == 2) outputCompletion.TrySetResult(true);
        }
        process.OutputDataReceived += CaptureOutput;
        process.ErrorDataReceived += CaptureOutput;
        var outputDrained = Task.CompletedTask;
        var started = false;
        var ready = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            started = process.Start();
            if (!started) throw new InvalidOperationException("Failed to start Moli.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            outputDrained = outputCompletion.Task;
            using var timeout = new CancellationTokenSource(options.StartupTimeout);
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
            try
            {
                await WaitForEndpointAsync(process, outputDrained, new Uri(endpoint, "json/version"), diagnostics, startup.Token);
                startup.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Moli did not become ready within {options.StartupTimeout}.{Environment.NewLine}{ReadDiagnostics(diagnostics)}");
            }

            ready = true;
            return new(process, endpoint, outputDrained);
        }
        finally
        {
            if (!ready)
            {
                try
                {
                    if (started) await StopProcessAsync(process, outputDrained);
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        disposal ??= DisposeCoreAsync();
        return new(disposal);
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await StopProcessAsync(process, outputDrained);
        }
        finally
        {
            process.Dispose();
        }
    }

    private static (ProcessStartInfo StartInfo, Uri Endpoint) CreateStartInfo(MoliOptions options, string executable)
    {
        // Release the ephemeral port immediately before launching; Moli reports any bind failure.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var arguments = $"serve --host 127.0.0.1 --port {port.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            if (options.EnableLayout) arguments += " --layout";
            if (options.LoadResources) arguments += " --resource";
            var info = new ProcessStartInfo(executable, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            return (info, new Uri($"http://127.0.0.1:{port}/"));
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task WaitForEndpointAsync(Process process, Task outputDrained, Uri endpoint, StringBuilder diagnostics, CancellationToken cancellationToken)
    {
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                await WaitForExitAsync(process, outputDrained, cancellationToken);
                throw new InvalidOperationException($"Moli exited with code {process.ExitCode}.{Environment.NewLine}{ReadDiagnostics(diagnostics)}");
            }
            try
            {
                using var response = await client.GetAsync(endpoint, cancellationToken);
                response.EnsureSuccessStatusCode();
                using var content = await response.Content.ReadAsStreamAsync();
                using var version = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
                var address = version.RootElement.GetProperty("webSocketDebuggerUrl").GetString();
                if (string.IsNullOrWhiteSpace(address)) throw new InvalidOperationException("Moli returned an empty CDP WebSocket endpoint.");
                if (process.HasExited) continue;
                return;
            }
            catch (HttpRequestException ex) when (IsConnectionFailure(ex))
            {
                // Connection refused is expected while the owned server is starting.
            }
            await Task.Delay(100, cancellationToken);
        }
    }

    private static bool IsConnectionFailure(HttpRequestException exception)
    {
#if NETSTANDARD2_0
        return exception.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused }
            or WebException { Status: WebExceptionStatus.ConnectFailure };
#else
        return exception.HttpRequestError == HttpRequestError.ConnectionError;
#endif
    }

    private static async Task StopProcessAsync(Process process, Task outputDrained)
    {
        if (!process.HasExited)
        {
            try
            {
#if NETSTANDARD2_0
                process.Kill();
#else
                process.Kill(entireProcessTree: true);
#endif
            }
            catch (InvalidOperationException) when (process.HasExited) { }
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await WaitForExitAsync(process, outputDrained, timeout.Token); }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException("Moli did not exit and close its output streams within five seconds.");
        }
    }

    private static async Task WaitForExitAsync(Process process, Task outputDrained, CancellationToken cancellationToken)
    {
#if NETSTANDARD2_0
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler onExit = (_, _) => exited.TrySetResult(true);
        process.Exited += onExit;
        try
        {
            process.EnableRaisingEvents = true;
            if (process.HasExited) exited.TrySetResult(true);
            using var registration = cancellationToken.Register(() => canceled.TrySetCanceled());
            var completed = await Task.WhenAny(Task.WhenAll(exited.Task, outputDrained), canceled.Task);
            await completed;
        }
        finally
        {
            process.Exited -= onExit;
        }
#else
        await process.WaitForExitAsync(cancellationToken);
#endif
    }

    private static void AppendDiagnostic(StringBuilder diagnostics, string? line)
    {
        if (line is null) return;
        lock (diagnostics)
        {
            diagnostics.AppendLine(line);
            if (diagnostics.Length > 8192) diagnostics.Remove(0, diagnostics.Length - 8192);
        }
    }

    private static string ReadDiagnostics(StringBuilder diagnostics)
    {
        lock (diagnostics) return diagnostics.ToString();
    }
}
