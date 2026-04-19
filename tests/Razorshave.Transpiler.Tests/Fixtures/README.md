# Transpiler Test Fixtures

Committed reference inputs for Razorshave's transpiler tests.

## Layout

Each fixture is a sub-folder containing:

| File | Role |
|---|---|
| `Input.razor` | Original Razor source. Committed for human reference — **not** consumed by the transpiler. |
| `Input.g.cs` | What the Razor source generator emits for that component. **This is the transpiler input.** |
| `Output.verified.js` | (added in step 5.3+) Expected JS output. Verify compares the real transpiler output against this. |

## Why we commit `Input.g.cs`

Microsoft can change Razor's emission between SDK patches — sequence numbers,
attribute shapes, code formatting, new implicit usings. Committing the generated
C# pins the transpiler's input so snapshot tests stay deterministic. When the
SDK moves we regenerate **on purpose** and review the diff.

## Regenerating

Run from repo root:

```bash
dotnet run tools/RegenerateFixtures.cs
```

The script builds `e2e/KitchenSink.Client/` with `EmitCompilerGeneratedFiles=true`
and copies each target's `.razor` + `.razor.g.cs` into the matching fixture
folder. It's idempotent — safe to run twice.

**Before regenerating intentionally** (SDK upgrade, adding a new fixture):

1. Note the current SDK version (`dotnet --version` — pinned via `global.json`)
2. Upgrade or change what you need
3. Run the script
4. `git diff` the fixtures — understand every change
5. Update the snapshot baselines in 5.3+ accordingly

## Current fixtures

Generated with SDK **10.0.201** (`global.json`) against .NET 10 Blazor Server template.

| Fixture | Source | Demonstrates |
|---|---|---|
| `counter/` | Components/Pages/Counter.razor | State, event handler (`MouseEventArgs`), `@page`, `@rendermode`, simple method |
| `weather/` | Components/Pages/Weather.razor | `@if`/`@foreach` as plain C#, nested class, `[StreamRendering]`, `async OnInitializedAsync`, LINQ, object-init |
| `mainlayout/` | Components/Layout/MainLayout.razor | `LayoutComponentBase`, `Body` property, nested component (`<NavMenu>`) |

## Don't edit the upstream files in KitchenSink

`Counter.razor`, `Weather.razor`, `MainLayout.razor` in `e2e/KitchenSink.Client/`
are the sources these fixtures mirror. If you change them, re-run the regen
script — otherwise the fixtures drift from "what `dotnet new blazor` produces"
and the snapshot assumptions break.

If you want to test a feature the template doesn't cover (e.g. `@bind`,
`@inject`, generic components), **add a new `.razor` under KitchenSink's
Components folder** and extend the `targets` list in
`tools/RegenerateFixtures.cs`. That keeps KitchenSink as the single source.
