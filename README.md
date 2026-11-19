# Moli.Net

A .NET Standard 2.0 / .NET 10 library that downloads, caches, starts, and stops [Moli](https://github.com/lexmount/moli). It exposes a CDP endpoint; use whichever automation client you prefer.

## Quick start

Install the .NET 10 SDK, then run in PowerShell:

```powershell
dotnet run --project .\examples\Moli.Example
```

On first use, the library downloads the pinned Moli release (currently **1.1.26**), verifies the archive's SHA-256 against GitHub release metadata, and caches the extracted distribution. Later runs reuse it. Automatic installation supports **Windows and glibc-based Linux, on x64 and ARM64**. Linux executable permissions are set automatically; no manual Moli install, `tar`, or `chmod` command is needed. Alpine/musl is not supported by the upstream GNU/Linux binaries. macOS still requires an existing `ExecutablePath`.

The cache lives under `%LOCALAPPDATA%\Moli.Net` on Windows and `$XDG_DATA_HOME/Moli.Net` (default `~/.local/share/Moli.Net`) on Linux. Linux and Windows installations are stored separately.

On Linux, use the same API or run the example from the workspace:

```sh
sudo apt-get update && sudo apt-get install -y libfontconfig1
dotnet run --project examples/Moli.Example
```

Moli's Linux binary requires glibc and native fontconfig (`libfontconfig1` on Debian/Ubuntu). Install system prerequisites yourself or in your base image; the library never runs a package manager or requests elevated privileges.

The offline example uses Playwright to fill a textbox and click a button, printing `Hello, Karl!`. No `playwright install` or Chromium download is needed. See the Playwright usage below to navigate a real site.

## Library usage (no Playwright dependency)

Add a project reference to `src\Moli.Net\Moli.Net.csproj`:

```csharp
using Moli.Net;

await using var moli = await MoliServer.StartAsync();
Console.WriteLine(moli.Endpoint); // http://127.0.0.1:<automatic-port>/
```

To only install Moli without launching it:

```csharp
var executable = await MoliInstaller.InstallAsync();
```

## Connect with Playwright

Add `Microsoft.Playwright` to your application, not the library:

```csharp
using Microsoft.Playwright;
using Moli.Net;

await using var moli = await MoliServer.StartAsync();
using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.ConnectOverCDPAsync(
    moli.Endpoint.AbsoluteUri, new() { Timeout = 10_000 });
var page = await browser.Contexts.Single().NewPageAsync();
await page.GotoAsync("https://example.com", new() { Timeout = 15_000 });
Console.WriteLine(await page.Locator("body").InnerTextAsync());
```

Declaration order matters: disconnect your automation client before disposing the server.

## Options and lifecycle

- `ExecutablePath`: use an existing executable instead of downloading; set `"moli"` to search PATH.
- `Version`: select a three-part release version when auto-installing. Defaults to `MoliInstaller.DefaultVersion`; upgrades are explicit, not silently tracked through `latest`.
- `StartupTimeout`: 30 seconds for server readiness, excluding installation. Installation has a separate three-minute limit, including cache-lock waits. Both accept cancellation tokens.
- `EnableLayout`: defaults to `true` for Playwright clicking, real geometry, and screenshots. Disable for DOM-only workloads.
- `LoadResources`: defaults to `false`; enable optional images, fonts, and media when needed.
- Each instance uses its own process and an automatically selected loopback port. An ephemeral port is reserved and released before launch; a competing bind can still cause startup to fail.
- Startup failures include captured process output where available. Download, integrity, HTTP, and cache errors propagate explicitly. Fix TLS trust/proxy configuration rather than disabling certificate validation.
- Dispose the server to terminate only its owned process. Forced termination does not guarantee profile/cookie flushing. The server does not own your automation client.
- The .NET 10 build also terminates descendants of the owned process. .NET Standard 2.0 has no process-tree termination API, so that build terminates only the Moli process itself.
- Moli is its own engine. CDP compatibility does not guarantee full Chromium/Playwright feature or rendering parity.
- For DOM text after mutations, prefer `TextContentAsync`. Moli's `InnerTextAsync` can reflect a previously frozen layout snapshot.

## Validation

```powershell
dotnet test .\tests\Moli.Playwright.Tests --blame-hang-timeout 60s
dotnet pack .\src\Moli.Net -c Release
dotnet list .\src\Moli.Net package --include-transitive
```

Tests run on .NET 10 and, on Windows, .NET Framework 4.8. On Linux, an additional .NET 8 test host selects the library's .NET Standard 2.0 target; this host can roll forward to the installed .NET 10 runtime. Install the .NET Framework 4.8 runtime to run the legacy Windows tests. They use real Moli and cover auto-install/cache reuse, DOM interaction, PNG capture, process cleanup, DOM-only mode, invalid options/version, a missing executable, startup timeout/early-exit diagnostics, and pre-launch cancellation. Only the example/tests reference Playwright.

To reproduce Linux validation using Podman from PowerShell:

```powershell
podman build -f .\tests\linux\Containerfile -t moli-net-linux-test .
podman run --rm --timeout 600 moli-net-linux-test
```

The container explicitly installs `libfontconfig1` on the Ubuntu-based .NET SDK image. It starts without a Moli cache, tests the .NET 10 implementation, then uses the .NET 8 test host (rolling forward to .NET 10) to test the .NET Standard 2.0 implementation with a separate empty cache. It also runs the Playwright example. Native system-package installation belongs to the Containerfile, not the library.

Validated on Ubuntu 24.04 x64 in Podman: both library targets pass the integration suite, including browser interaction, PNG capture, cache reuse, and owned-process cleanup. ARM64 release selection is implemented but has not been runtime-tested.
