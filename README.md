# Razorshave

> Write Blazor. Deploy as SPA.

Build-time compiler that turns Blazor components into static JavaScript — no WASM runtime, no SignalR, no server. Output is a static bundle deployable on any web server (nginx, S3, Cloudflare Pages).

**Status:** Pre-M0. The bootstrap milestone — first end-to-end project transpile (`dotnet new blazor` → `dist/`) — is implemented end to end through step 5.13 of `planning/RAZORSHAVE-BOOTSTRAP.md`. M0 acceptance (5.14) is the next step.

## Quick start (developer loop)

You need .NET 10 SDK and Node 22+.

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
├── Razorshave.Cli/            dotnet-tool + MSBuild task that orchestrates the pipeline
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

At the time of writing: 85 tests across both runners.

## License

MIT
