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

## Runnable samples

```sh
dotnet run --project examples/Moli.Playwright.Sample
dotnet run --project examples/Moli.PuppeteerSharp.Sample
dotnet run --project examples/Moli.Selenium.Sample
dotnet run --project examples/Moli.Pdf.Sample
```

The Playwright, PuppeteerSharp, and Selenium samples fill a textbox and click a button offline, printing `Hello, Karl!`. The PDF sample serves an ASP.NET Core page on `http://127.0.0.1:5187`, captures the rendered HTML in middleware, and returns it as a PDF. No `playwright install` or Chrome download needed.
