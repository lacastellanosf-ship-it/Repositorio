using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.Web;
using Citaspp.Web.Client;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
#if CITASPP_STANDALONE
builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
#endif
builder.Services.AddScoped<LocalApiMessageHandler>();
builder.Services.AddScoped(sp => new HttpClient(
    sp.GetRequiredService<LocalApiMessageHandler>())
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});

await builder.Build().RunAsync();
