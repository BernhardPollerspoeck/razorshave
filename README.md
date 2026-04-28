# Razorshave

> Write Blazor. Deploy as SPA.

Build-time compiler that turns Blazor components into static JavaScript — no WASM runtime, no SignalR, no server. Output is a static bundle deployable on any web server (nginx, S3, Cloudflare Pages).

**Status:** `0.2.1` on NuGet — public preview. 316 tests green (197 JS + 119 C#); KitchenSink demo transpiles end-to-end. API and emitters are shaping up but still expect change before `1.0`.

## Getting started

You need the **.NET 10 SDK** — nothing else. The `Razorshave.Cli` package ships the JS runtime and the esbuild binaries for Windows, Linux, macOS (x64 + arm64) inside the NuGet itself.

### 1. New project

```bash
dotnet new blazor -o MyApp
cd MyApp
```

### 2. Add the three Razorshave packages

```bash
dotnet add package Razorshave.Abstractions
dotnet add package Razorshave.Analyzer
dotnet add package Razorshave.Cli
```

Your `.csproj` should now look something like:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <!-- Razorshave needs the Razor generator to persist .razor.g.cs on disk. -->
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Razorshave.Abstractions" Version="0.2.1" />
    <PackageReference Include="Razorshave.Analyzer"     Version="0.2.1" />
    <PackageReference Include="Razorshave.Cli"          Version="0.2.1" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

### 3. Wire Razorshave services in `Program.cs`

```csharp
using Razorshave.Abstractions;

var builder = WebApplication.CreateBuilder(args);

// Registers IStore<T>, ILocalStorage, ISessionStorage, ICookieStore.
// Server-side only — the transpiled SPA uses the JS runtime's equivalents.
builder.Services.AddRazorshave();

// Your own [Client] services — register here so the Blazor Server dev loop
// (dotnet run) can resolve them. The transpiled SPA auto-registers them
// on the JS side from the [Client] marker.
builder.Services.AddHttpClient<IWeatherApi, WeatherApi>();

// ... rest of the standard Blazor Server setup ...
```

### 4. Write a `[Client]`-marked API client

```csharp
using Razorshave.Abstractions;

[Client]
public sealed class WeatherApi(HttpClient http) : ApiClient(http), IWeatherApi
{
    public Task<WeatherForecast[]> GetForecastsAsync()
        => Get<WeatherForecast[]>("https://…")!;
}
```

### 5. Two build modes

```bash
# Normal dev loop: Blazor Server, full debugger, Hot Reload.
dotnet run

# Production: transpile to a static JavaScript SPA in dist/.
dotnet build -c Razorshave
```

The `Razorshave` configuration is opt-in — `Debug` and `Release` stay vanilla Blazor. The transpiler only runs when you ask for it.

### 6. Ship

`dist/` contains `index.html`, a content-hashed `main.XXXX.js`, and your `wwwroot/` assets. Drop it on any static host — nginx, S3, Cloudflare Pages, GitHub Pages.

### What the analyzer tells you

If you write C# the transpiler cannot emit yet, the analyzer fires at edit-time:

- **RZS2001** — unsupported expression (e.g. `stackalloc`)
- **RZS2002** — unsupported statement
- **RZS2003** — unsupported pattern (list, declaration, recursive, binary patterns inside `is` / `switch`)
- **RZS3001** — your component's name shadows a runtime component (`NavLink`, `Router`, `PageTitle`)

Every diagnostic carries a link to the issue tracker — if you hit one, file a repro so the gap can be closed. No silent `/* TODO */` in the generated JS: if the analyzer doesn't catch a gap, the transpiler throws as a hard build error (`RZS9001`) with file + line + the same issue link. If it compiles, it runs.

### Configuration knobs

Set these in your `.csproj` (under `<PropertyGroup>`):

```xml
<RazorshaveBasePath>/myapp/</RazorshaveBasePath>  <!-- default "/" -->
<RazorshaveTitle>My App</RazorshaveTitle>          <!-- default $(AssemblyName) -->
```

`RazorshaveBasePath` prefixes every emitted asset URL — set this when deploying under a sub-path so deep-link requests for `/myapp/legal/agb` still resolve `main.X.js` correctly. `RazorshaveTitle` is the static `<title>` in `index.html`; per-page `<PageTitle>` components override it at runtime.

---

## Quick start (developer loop)

If you're hacking on Razorshave itself, the mono-repo has the full C# + JS test suite. You need .NET 10 SDK and Node 22+.

```bash
# Clone & restore
dotnet restore
(cd src/Razorshave.Runtime && npm install)

# Run the test suite (C# + JS)
dotnet test
(cd src/Razorshave.Runtime && npm test)

# Transpile the kitchen-sink demo end to end
dotnet build -c Razorshave e2e/KitchenSink.Client/KitchenSink.Client.csproj

# Serve the output
npx --yes serve e2e/KitchenSink.Client/dist
```

Open `http://localhost:3000` — you get the transpiled Blazor demo running as a pure-JS SPA.

## User workflow

Any project that references `Razorshave.Abstractions` and imports the
`Razorshave.Cli` MSBuild targets gets the full pipeline:

1. Write Blazor components as usual: `.razor` files, `@page`, `@inject`, `@code`.
2. Use the `ApiClient`-based data access pattern for everything that hits an API.
3. Normal dev: `dotnet run` → Blazor Server, Hot Reload, full debugger.
4. Production build: `dotnet build -c Razorshave` → `dist/` with `index.html`, a content-hashed `main.XXXX.js`, and your `wwwroot/` assets.
5. Deploy `dist/` as static files.

The `Razorshave` build configuration is the opt-in. `Debug` and `Release` remain untouched — the transpiler doesn't run in your everyday dev loop.

## Repository layout

```
src/
├── Razorshave.Abstractions/   Attributes and base types user code references
│                              ([Client], [ApiRoute], ApiClient, IStore<T>)
├── Razorshave.Cli/            MSBuild task package that orchestrates the pipeline
│   ├── Transpiler/            C# → JS emitters (class, field, method, expression,
│   │                          statement, render-tree, etc.)
│   ├── BuildCommand.cs        The build pipeline: build → transpile → bundle → dist/
│   ├── RazorshaveTranspileTask.cs  MSBuild-task wrapper around BuildCommand
│   └── build/Razorshave.Cli.targets  MSBuild integration — triggers on -c Razorshave
├── Razorshave.Analyzer/       Roslyn analyzer (stubbed, M0+ scope)
└── Razorshave.Runtime/        JS runtime bundled into every transpiled SPA
    ├── src/h.js, component.js, vdom.js,
    │   router.js, navigation-manager.js,
    │   container.js, api-client.js,
    │   events.js, mount.js, index.js
    └── src/builtins/page-title.js, nav-link.js

tests/
├── Razorshave.Transpiler.Tests/  Verify-based snapshot tests + CLI integration tests
├── Razorshave.Analyzer.Tests/
└── Razorshave.SourceGenerator.Tests/

e2e/
└── KitchenSink.Client/        Demo project — the dotnet-new-blazor template with
                               one tweak: Weather.razor uses IWeatherApi instead of
                               inline mock data.

tools/
├── RoslynExplorer.cs          File-based app for inspecting Razor-generated C#
└── RegenerateFixtures.cs      Copies the current kitchen-sink .razor.g.cs snapshots
                               into tests/…/Fixtures/ so the snapshot tests stay
                               deterministic across SDK updates

planning/                       Design docs (RAZORSHAVE-*.md)
```

## Running the suites

```bash
# Full C# test suite — includes snapshot tests and CLI integration tests
dotnet test

# JS runtime tests (Vitest + jsdom)
cd src/Razorshave.Runtime && npm test
```

At the time of writing: 309 tests across both runners (191 JS + 118 C#).

## License

MIT
