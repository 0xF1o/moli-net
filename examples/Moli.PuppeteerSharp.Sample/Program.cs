using Moli.Net;
using PuppeteerSharp;

await using var moli = await MoliServer.StartAsync();
await using var browser = await Puppeteer.ConnectAsync(new ConnectOptions { BrowserURL = moli.Endpoint.AbsoluteUri, DefaultViewport = null });
var page = await browser.NewPageAsync();
page.DefaultTimeout = 10_000;
page.DefaultNavigationTimeout = 15_000;

await page.SetContentAsync("""
    <title>Moli + PuppeteerSharp</title>
    <label>Name <input id="name"></label>
    <button id="greet" onclick="document.querySelector('output').textContent = 'Hello, ' + document.querySelector('input').value + '!'">Greet</button>
    <output></output>
    """);
await page.TypeAsync("#name", "Karl");
await page.ClickAsync("#greet");
await page.WaitForFunctionAsync("() => document.querySelector('output').textContent === 'Hello, Karl!'", new WaitForFunctionOptions { Timeout = 5_000 });

Console.WriteLine(await page.GetTitleAsync());
Console.WriteLine(await page.EvaluateFunctionAsync<string>("() => document.querySelector('output').textContent"));
