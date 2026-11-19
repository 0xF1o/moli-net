using Microsoft.Playwright;
using Moli.Net;

await using var moli = await MoliServer.StartAsync();
using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.ConnectOverCDPAsync(moli.Endpoint.AbsoluteUri, new() { Timeout = 10_000 });
var context = browser.Contexts.Single();
var page = await context.NewPageAsync();
page.SetDefaultTimeout(10_000);
page.SetDefaultNavigationTimeout(15_000);

await page.SetContentAsync("""
    <title>Moli + .NET</title>
    <label>Name <input id="name"></label>
    <button onclick="document.querySelector('output').textContent = 'Hello, ' + document.querySelector('input').value + '!'">Greet</button>
    <output></output>
    """);
await page.GetByLabel("Name").FillAsync("Karl");
await page.GetByRole(AriaRole.Button, new() { Name = "Greet" }).ClickAsync();
await Assertions.Expect(page.Locator("output")).ToHaveTextAsync("Hello, Karl!", new() { Timeout = 5_000 });

Console.WriteLine(await page.TitleAsync());
Console.WriteLine(await page.Locator("output").TextContentAsync());