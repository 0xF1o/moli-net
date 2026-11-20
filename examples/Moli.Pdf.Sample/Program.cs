using System.Text;
using Microsoft.Playwright;
using Moli.Net;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

await using var renderer = await PdfRenderer.StartAsync();

app.Use(async (context, next) =>
{
    var original = context.Response.Body;
    using var buffer = new MemoryStream();
    context.Response.Body = buffer;
    await next();
    context.Response.Body = original;
    if (!(context.Response.ContentType?.StartsWith("text/html") ?? false))
    {
        buffer.Position = 0;
        await buffer.CopyToAsync(original);
        return;
    }
    var pdf = await renderer.RenderAsync(Encoding.UTF8.GetString(buffer.ToArray()));
    context.Response.ContentType = "application/pdf";
    context.Response.Headers.ContentDisposition = "inline; filename=page.pdf";
    context.Response.ContentLength = pdf.Length;
    await original.WriteAsync(pdf);
});

app.MapGet("/", () => Results.Content("""
    <style>
        @page { size: A4; margin: 1.5cm }
        body { font-family: sans-serif; color: #222 }
        table { width: 100%; border-collapse: collapse }
        th, td { padding: 6px; border-bottom: 1px solid #ccc; text-align: left }
        td:last-child { text-align: right }
    </style>
    <h1>Invoice 2026-001</h1>
    <p>Rendered by ASP.NET Core, intercepted by middleware, printed to PDF by Moli.</p>
    <table>
        <tr><th>Item</th><th>Price</th></tr>
        <tr><td>Headless browser</td><td>1 EUR</td></tr>
        <tr><td>PDF export</td><td>0 EUR</td></tr>
        <tr><td><strong>Total</strong></td><td><strong>1 EUR</strong></td></tr>
    </table>
    """, "text/html"));

await app.RunAsync("http://0.0.0.0:5187");

sealed class PdfRenderer : IAsyncDisposable
{
    private readonly MoliServer moli;
    private readonly IPlaywright playwright;
    private readonly IBrowser browser;
    // shortcut: one PDF at a time, upgrade to a page pool if throughput matters
    private readonly SemaphoreSlim gate = new(1, 1);

    private PdfRenderer(MoliServer moli, IPlaywright playwright, IBrowser browser)
    {
        this.moli = moli;
        this.playwright = playwright;
        this.browser = browser;
    }

    public static async Task<PdfRenderer> StartAsync()
    {
        var moli = await MoliServer.StartAsync();
        try
        {
            var playwright = await Playwright.CreateAsync();
            try
            {
                var browser = await playwright.Chromium.ConnectOverCDPAsync(moli.Endpoint.AbsoluteUri, new() { Timeout = 10_000 });
                return new(moli, playwright, browser);
            }
            catch
            {
                playwright.Dispose();
                throw;
            }
        }
        catch
        {
            await moli.DisposeAsync();
            throw;
        }
    }

    public async Task<byte[]> RenderAsync(string html)
    {
        await gate.WaitAsync();
        try
        {
            var page = await browser.Contexts[0].NewPageAsync();
            try
            {
                await page.SetContentAsync(html);
                return await page.PdfAsync(new() { Format = "A4", PrintBackground = true });
            }
            finally
            {
                await page.CloseAsync();
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await browser.DisposeAsync();
        playwright.Dispose();
        await moli.DisposeAsync();
    }
}
