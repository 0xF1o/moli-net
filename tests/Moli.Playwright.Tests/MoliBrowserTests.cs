using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Playwright;
using Moli.Net;
using Xunit;

namespace Moli.Playwright.Tests;

public sealed class MoliBrowserTests
{
    [Fact]
    public async Task StartsInteractsAndStopsOwnedProcess()
    {
        var moli = await MoliServer.StartAsync();
        using var process = Process.GetProcessById(moli.ProcessId);
        await using (moli)
        {
            using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.ConnectOverCDPAsync(moli.Endpoint.AbsoluteUri, new() { Timeout = 5_000 });
            var page = await browser.Contexts.Single().NewPageAsync();
            page.SetDefaultTimeout(5_000);
            await page.SetContentAsync("""
                <title>Integration</title>
                <label>Name <input></label>
                <button onclick="document.querySelector('output').textContent = document.querySelector('input').value">Save</button>
                <output></output>
                """);
            await page.GetByLabel("Name").FillAsync("Flo");
            Assert.Equal("Flo", await page.GetByLabel("Name").InputValueAsync());
            await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
            Assert.Equal("Integration", await page.TitleAsync());
            await Assertions.Expect(page.Locator("output")).ToHaveTextAsync("Flo", new() { Timeout = 5_000 });
            var screenshot = await page.ScreenshotAsync(new() { Timeout = 5_000 });
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, screenshot.Take(8));
        }
        Assert.True(process.HasExited);
        await moli.DisposeAsync();
    }

    [Fact]
    public async Task SupportsDomOnlyMode()
    {
        await using var moli = await MoliServer.StartAsync(new() { EnableLayout = false });
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.ConnectOverCDPAsync(moli.Endpoint.AbsoluteUri, new() { Timeout = 5_000 });
        var page = await browser.Contexts.Single().NewPageAsync();
        await page.SetContentAsync("<main>DOM only</main>", new() { Timeout = 5_000 });
        Assert.Equal("DOM only", await page.Locator("main").TextContentAsync(new() { Timeout = 5_000 }));
    }

    [Fact]
    public async Task ReportsMissingExecutable()
    {
        await Assert.ThrowsAsync<Win32Exception>(() => MoliServer.StartAsync(new() { ExecutablePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "missing.exe") }));
    }

    [Fact]
    public async Task RejectsInvalidTimeout()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => MoliServer.StartAsync(new() { StartupTimeout = TimeSpan.Zero }));
    }

    [Fact]
    public async Task HonorsCancellationBeforeLaunch()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MoliServer.StartAsync(cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task ReportsStartupTimeout()
    {
        var executable = await MoliInstaller.InstallAsync();
        await Assert.ThrowsAsync<TimeoutException>(() => MoliServer.StartAsync(new() { ExecutablePath = executable, StartupTimeout = TimeSpan.FromTicks(1) }));
    }

    [Fact]
    public async Task ReportsEarlyProcessExitWithDiagnostics()
    {
        var executable = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "whoami.exe") : "whoami";
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => MoliServer.StartAsync(new() { ExecutablePath = executable }));
        Assert.StartsWith("Moli exited with code ", error.Message);
        Assert.NotEmpty(error.Message.Split(new[] { Environment.NewLine }, 2, StringSplitOptions.None)[1]);
    }

    [Theory]
    [InlineData(MoliInstaller.DefaultVersion)]
    [InlineData("1.1.25")]
    public async Task InstallerCachesVerifiedRelease(string version)
    {
        var first = await MoliInstaller.InstallAsync(version);
        var timestamp = File.GetLastWriteTimeUtc(first);
        var second = await MoliInstaller.InstallAsync(version);
        Assert.Equal(first, second);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(second));
        Assert.True(File.Exists(second));
    }

    [Fact]
    public async Task InstallerRejectsInvalidVersion()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => MoliInstaller.InstallAsync("../invalid"));
    }
}
