using Moli.Net;
using OpenQA.Selenium;
using OpenQA.Selenium.Remote;

await using var moli = await MoliServer.StartAsync();
using var driver = new RemoteWebDriver(moli.Endpoint, new MoliOptions());
driver.Navigate().GoToUrl("about:blank");
((IJavaScriptExecutor)driver).ExecuteScript("""
    document.title = 'Moli + Selenium';
    document.body.innerHTML = `<label>Name <input id="name"></label>
        <button id="greet" onclick="document.querySelector('output').textContent = 'Hello, ' + document.querySelector('input').value + '!'">Greet</button>
        <output></output>`;
    """);
driver.FindElement(By.Id("name")).SendKeys("Karl");
driver.FindElement(By.Id("greet")).Click();

Console.WriteLine(driver.Title);
Console.WriteLine(driver.FindElement(By.TagName("output")).Text);

// Moli serves WebDriver Classic natively, but only matches browserName "moli".
sealed class MoliOptions : DriverOptions
{
    public MoliOptions() => BrowserName = "moli";
    public override ICapabilities ToCapabilities() => GenerateDesiredCapabilities(true);
    public override void AddAdditionalOption(string optionName, object optionValue) => base.AddAdditionalOption(optionName, optionValue);
}
