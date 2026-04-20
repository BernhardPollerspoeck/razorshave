using KitchenSink.Client;
using KitchenSink.Client.Components;
using Razorshave.Abstractions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Registers Razorshave built-ins (IStore<T>, ILocalStorage, ISessionStorage,
// ICookieStore) so components resolve them when the app runs as a Blazor
// Server dev host. In the transpiled SPA the JS runtime takes over.
builder.Services.AddRazorshave();

// Typed HttpClient for the [Client] WeatherApi — so that the Blazor-Server
// dev host can resolve IWeatherApi and hit open-meteo directly. In the
// transpiled SPA this registration is ignored; the JS runtime constructs
// WeatherApi with a null HttpClient (the transpiled methods use fetch()
// directly, not this.HttpClient).
builder.Services.AddHttpClient<IWeatherApi, WeatherApi>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
