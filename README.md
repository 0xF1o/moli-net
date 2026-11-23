# Moli.Net

Starts and stops [Moli](https://github.com/lexmount/moli), a headless browser that speaks CDP. Use Playwright, PuppeteerSharp, or any other CDP client.

On first use it downloads the pinned Moli release, verifies its SHA-256, and caches it. Linux needs glibc and **libfontconfig1**; macOS needs an existing **ExecutablePath**.

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

## Samples

See [examples/README.md](examples/README.md) for runnable samples.
