using Adventures.Browser.Client;
using Adventures.Browser.Web;
using Adventures.Browser.Web.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

// Composition root: the browser API address is the installation's setting (wwwroot/appsettings.json on the server).
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Shell>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var address = builder.Configuration["Api:BaseUrl"] ?? throw new InvalidOperationException("Api:BaseUrl is not set in appsettings.json.");
builder.Services.AddAdventuresBrowserClient(new Uri(address));
builder.Services.AddSingleton<ISiteListStore, LocalStorageSiteListStore>();
builder.Services.AddSingleton<BrowserShellViewModel>();

await builder.Build().RunAsync();
