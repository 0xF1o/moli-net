# Moli.Net

Starts and stops [Moli](https://github.com/lexmount/moli), a headless browser that speaks CDP. Use Playwright, PuppeteerSharp, or any other CDP client.

On first use it downloads the pinned Moli release (currently **1.1.26**), verifies its SHA-256, and caches it. Linux needs glibc and `libfontconfig1`; macOS needs an existing `ExecutablePath`.

## Minimal example

```csharp
using Microsoft.Playwright;
using Moli.Net;

await using var moli = await MoliServer.StartAsync();
using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.ConnectOverCDPAsync(moli.Endpoint.AbsoluteUri, new() { Timeout = 10_000 });
var page = await browser.Contexts.Single().NewPageAsync();
await page.GotoAsync("https://example.com");
Console.WriteLine(await page.Locator("body").InnerTextAsync());
```

Dispose the client before the server.

With PuppeteerSharp, use `Puppeteer.ConnectAsync(new ConnectOptions { BrowserURL = moli.Endpoint.AbsoluteUri, DefaultViewport = null })` instead.

## Runnable samples

```sh
dotnet run --project examples/Moli.Playwright.Sample
dotnet run --project examples/Moli.PuppeteerSharp.Sample
```

Both fill a textbox and click a button offline, printing `Hello, Karl!`. No `playwright install` or Chrome download needed.

## Options

`MoliServer.StartAsync(new MoliOptions { ... })`:

- `ExecutablePath`: use an existing executable ("moli" searches PATH) instead of downloading.
- `Version`: Moli release version to install.
- `StartupTimeout`: default 30 seconds.
- `EnableLayout`: default `true`; needed for clicking, geometry, and screenshots.
- `LoadResources`: default `false`; enable images, fonts, and media.

Moli is its own engine, so CDP compatibility does not guarantee full Chromium/Playwright parity.
