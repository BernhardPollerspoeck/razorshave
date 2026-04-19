# Razorshave: M0 — Proof-of-Concept

> Erster konkreter Milestone. Ziel: do-or-don't-Entscheidung treffen können.

**Status:** Design-Draft
**Parent:** RAZORSHAVE.md
**Datum:** 2026-04-19

---

## Ziel

"**Das minimal angepasste Blazor-Template läuft transpiliert im Browser.**"

Wenn M0 läuft, ist bewiesen dass Razorshave ein realistischer Weg ist. Danach lässt sich abschätzen was für v0.1 noch fehlt und wie der Aufwand wirklich aussieht.

**Was M0 nicht ist:**
- Kein feature-complete Release
- Kein Öffentlich-Release
- Keine optimierte Bundle-Size
- Keine produktionsreife App-Nutzung

**Was M0 ist:**
- Ehrlicher Machbarkeits-Check am realen Blazor-Code
- Foundation für weitere Arbeit
- Entscheidungspunkt: continue, pivot oder drop

---

## User-Setup (das M0 testet)

### Projekt-Struktur

```
Razorshave.M0.Demo/
├── Razorshave.M0.Demo.Client/      ← Razorshave-Projekt (basiert auf dotnet new blazor)
│   ├── Components/
│   │   ├── App.razor               ← unverändert
│   │   ├── Routes.razor            ← unverändert
│   │   ├── Layout/
│   │   │   ├── MainLayout.razor    ← unverändert
│   │   │   └── NavMenu.razor       ← unverändert
│   │   └── Pages/
│   │       ├── Home.razor          ← unverändert
│   │       ├── Counter.razor       ← unverändert
│   │       └── Weather.razor       ← angepasst: nutzt IWeatherApi
│   ├── Services/
│   │   └── WeatherApi.cs           ← neu: manuelle ApiClient-Impl
│   ├── Program.cs                  ← +1 Zeile: AddRazorshave()
│   └── wwwroot/                    ← unverändert (CSS, favicon)
│
└── Razorshave.M0.Demo.Server/      ← Minimal ASP.NET Core
    ├── Controllers/
    │   └── WeatherForecastController.cs   ← gibt Mock-Daten zurück
    └── Program.cs
```

### Die einzige Änderung in Program.cs

```csharp
// Original-Template
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// M0-Version: eine Zeile mehr
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
    
builder.Services.AddRazorshave();  // ← NEU
```

### Weather.razor-Anpassung

Original (In-Memory-Mock):
```csharp
@code {
    private WeatherForecast[]? forecasts;
    
    protected override async Task OnInitializedAsync() {
        await Task.Delay(500);
        var startDate = DateOnly.FromDateTime(DateTime.Now);
        var summaries = new[] { "Freezing", "Bracing", "Chilly", ... };
        forecasts = Enumerable.Range(1, 5).Select(index => new WeatherForecast {
            Date = startDate.AddDays(index),
            TemperatureC = Random.Shared.Next(-20, 55),
            Summary = summaries[Random.Shared.Next(summaries.Length)]
        }).ToArray();
    }
}
```

M0-Version (ApiClient):
```csharp
@inject IWeatherApi WeatherApi

@code {
    private WeatherForecast[]? forecasts;
    
    protected override async Task OnInitializedAsync() {
        forecasts = await WeatherApi.GetForecastsAsync();
    }
}
```

Plus neue Klasse `WeatherApi.cs`:
```csharp
[Client]
public class WeatherApi : ApiClient, IWeatherApi
{
    public WeatherApi(HttpClient http) : base(http) { }
    
    public Task<WeatherForecast[]> GetForecastsAsync()
        => Get<WeatherForecast[]>("api/weather");
}

public interface IWeatherApi
{
    Task<WeatherForecast[]> GetForecastsAsync();
}
```

---

## Definition-of-Done

Alle Punkte müssen gelb-grün durchgespielt sein:

| # | Check | Status |
|---|---|---|
| 1 | `dotnet build -c Razorshave` läuft ohne Error durch | - |
| 2 | `dist/` enthält: index.html, app.[hash].js, app.[hash].css, favicon.ico, Assets | - |
| 3 | Static-Serve (z.B. `dotnet serve` in dist/) startet auf localhost | - |
| 4 | Browser auf localhost:xxxx zeigt Home-Page mit Welcome-Message | - |
| 5 | Nav zu /counter via NavLink-Klick → Counter lädt | - |
| 6 | Klick auf Counter-Button → Zahl erhöht sich | - |
| 7 | Nav zu /weather via NavLink-Klick → Weather lädt | - |
| 8 | Weather-Page zeigt Tabelle mit 5 Forecasts vom Mock-Server | - |
| 9 | NavMenu hebt aktive Page korrekt hervor (active-class) | - |
| 10 | Browser-Back-Button: zurück zu vorheriger Page | - |
| 11 | Browser-Forward-Button: wieder zu /weather | - |
| 12 | Page-Reload auf /counter direkt → funktioniert (Router erkennt URL) | - |
| 13 | Page-Reload auf /weather → API-Call erfolgt, Daten erscheinen | - |
| 14 | Scoped CSS funktioniert (Counter-Styles isoliert) | - |
| 15 | Globales CSS funktioniert (Bootstrap-Styles aus app.css) | - |

---

## Was M0 beweist

Bei allen Punkten grün ist bewiesen:

- **Transpiler** kann realistisches Blazor-Code transpilieren (nicht nur triviale Counter)
- **RenderTreeBuilder → h()-Mapping** funktioniert für Elements, Components, Attributes, Events, Text, Expressions
- **Router** funktioniert mit NavLink + History-API + Direct-URL-Entry
- **ApiClient** funktioniert mit async Lifecycle und realem HTTP-Call
- **VDOM-Diff** funktioniert für Listen (5 Forecasts), Text-Updates (Counter), Attribute-Updates (NavLink-active)
- **MSBuild-Integration** läuft zuverlässig als Teil des normalen Build-Flows
- **esbuild-Bundling** produziert deploybares, korrektes `dist/`
- **Scoped CSS-Pipeline** (piggy-back auf Microsoft) funktioniert
- **Component-Lifecycle** (OnInitialized, OnInitializedAsync, Dispose) feuert korrekt
- **DI-Container** kann Services injizieren (HttpClient, IWeatherApi, NavigationManager)

---

## Scope-Grenze: Was NICHT in M0

Explizit ausgeschlossen um Aufwand überschaubar zu halten:

**Sprache/Transpiler:**
- Operator-Overloading (keine Money-Class o.ä. in M0)
- decimal (nur primitive types + string)
- Pattern Matching außer basic if/else
- LINQ-Methoden außer was Weather-Array braucht
- Generics außer bei `List<T>`, `Task<T>`, `IStore<T>`
- Records mit Auto-Equality
- Indexer
- Multiple Constructors (auto-fail im Analyzer)

**Runtime:**
- Store (`IStore<T>`) — Counter hat State inline
- IJSRuntime / JS-Interop
- Error-Boundaries
- CascadingValue
- Alle EventArgs außer `MouseEventArgs` und simple Events
- @bind-Formen außer trivial
- LocationChangingHandler (v0.1)

**ApiClient:**
- Retry-Policy
- Timeout-Konfiguration
- CancellationToken
- FormData (File-Upload)
- Custom Header-Pipeline (nur Basic-Get reicht)

**Source-Generator:**
- `[ApiRoute]`-Auto-Generation — User schreibt `WeatherApi` manuell
- Source-Gen kommt in v0.1

**Build-Tooling:**
- `razorshave.json` Config (defaults reichen)
- Content-Hashing (nice-to-have, kein Blocker)
- Tree-Shaking (ok wenn Bundle 500 KB ist, wir optimieren später)

**Tests:**
- Kein vollständiger Coverage-Anspruch
- Kein Playwright-Setup
- Kein CI
- Ein einziger End-to-End-Smoke-Test manuell durchgespielt genügt

**Docs:**
- Kein öffentliches README
- Kein Website-Deployment
- Interne Docs (unsere bestehenden 5 Files) reichen

---

## Was gebraucht wird (Feature-Shopping-Liste)

Konkret was der Code haben muss damit M0 läuft.

### Transpiler-Features (Minimum für M0)

- Classes, Fields, Properties, Methods, Constructors (single)
- `@code`-Block-Content transpilieren
- if/else, for, foreach über Arrays
- Lambda-Expressions für Event-Handler
- async/await auf Task, Task<T>
- String-Interpolation
- Array-Literals, `new T[] { ... }`
- Collection-Initializer
- `@inject`-Attribute-Recognition → DI-Resolution
- `@page`-Attribute-Recognition → Route-Table-Entry
- `@rendermode`-Attribute IGNORIEREN (Feature: explizit kein Error werfen)
- RenderTreeBuilder-Walker für:
  - OpenElement/CloseElement
  - OpenComponent/CloseComponent
  - AddContent (text + expression)
  - AddAttribute (static + dynamic)
  - AddMarkupContent
- Basic allow-list Validator (erkennt DbContext, IConfiguration und ähnliche als Errors)

### Runtime-Features (Minimum für M0)

- Component-Base-Class mit:
  - onInit / onInitAsync lifecycle
  - stateHasChanged() + rAF scheduling
  - Props-Live-Getter
  - Render-to-VDOM via render()
  - Event-Handler-Wrapping mit auto-stateHasChanged
- h() Hyperscript
- VDOM-Diff:
  - Element-Attribute-Updates
  - Text-Node-Updates
  - Keyed-List-Diff (für Weather-Array)
  - Component-Instance-Reuse
- Router:
  - Route-Registration aus build-generated Route-Table
  - URL-Matching (simple paths, keine Constraints in M0)
  - pushState-Navigation
  - popState-Handling (Back/Forward)
  - NavigationManager: Uri, NavigateTo, LocationChanged
- NavLink-Component (active-class bei Route-Match)
- MouseEventArgs (für Click)
- DI-Container (Singleton-Only, Register/Resolve)
- ApiClient-Basis:
  - Get<T>(path) via fetch()
  - JSON-Parse
  - Basic Error auf non-2xx
  - Keine Hooks, Retry, Timeout, etc.

### Build-Pipeline (Minimum für M0)

- MSBuild-Target "Razorshave" das bei `-c Razorshave` triggert
- Einlesen von User-Sources + generated Sources via MSBuild-Properties
- Roslyn-Compilation aufbauen
- Validation-Pass (basic)
- Transpilation-Pass
- Runtime-Module einbinden
- esbuild-Aufruf zum Bundlen
- Output-Writing nach dist/
- wwwroot/ 1:1 Copy

---

## Aufwand-Rahmen

Realistische Schätzung mit Consors als Primary-Implementer und Bernhard als Architect/Reviewer:

| Bereich | Aufwand (Vollzeit-Äquivalent) |
|---|---|
| Transpiler-Core (Minimum-Subset) | 3-4 Wochen |
| Runtime (Component + VDOM + Router + NavLink + ApiClient-minimal) | 2-3 Wochen |
| MSBuild-Integration + esbuild-Pipeline | 1-2 Wochen |
| Source-Gen für `[ApiRoute]` — NICHT in M0, verschoben | 0 |
| Integration-Debug, Edge-Cases | 1-2 Wochen |
| **Summe** | **6-10 Wochen Vollzeit** |

**Realtime-Schätzung bei "Abend-Projekt"** (4-8h fokussierte Zeit pro Woche):
**6-12 Monate Wandzeit.**

Das ist nicht schlimm. Bedeutet aber dass Specs jetzt scharf genug sein müssen damit Consors autonom arbeiten kann wenn Bernhard mal 3 Wochen keine Zeit hat.

---

## Do-or-Don't-Entscheidung (nach M0)

Wenn M0 durch ist, kommt die informierte Entscheidung:

**Continue** — wenn:
- Alle DoD-Punkte grün
- Bundle-Size im Rahmen (< 400 KB gzipped für die Demo)
- Dev-Experience fühlt sich natürlich an
- Debugging im Browser nicht ekelig
- Keine fundamentalen Probleme die wir nicht vorhergesehen haben

**Pivot** — wenn:
- Funktioniert teilweise, aber irgendwas ist systematisch hässlich
- Bundle ist unerwartet groß (> 800 KB gzipped)
- Debug-Experience ist so schlecht dass User es nicht akzeptieren würden
- Einzelne Razor-Features sind doch komplizierter als gedacht

→ Entscheidung: Was pivoten? Scope reduzieren? Architektur neu denken?

**Drop** — wenn:
- Fundamentales Problem das wir nicht lösen können
- Microsoft ändert irgendwas in Razor das uns die Grundlage zieht
- Bundle ist so groß dass der Pitch nicht mehr funktioniert
- Cost-Benefit rechnet sich nicht

→ Ehrlich ablegen. Projektleiche akzeptieren. Learnings mitnehmen.

---

## Nicht festgelegt

**Namens-Finalisierung:** "Razorshave" ist tentativ. Vor Public-Release finalisieren.

**Repo-Setup:** wird vor M0-Start entschieden (Mono-Repo Struktur, Hosting auf GitHub/Gitea, CI-Template)

**Versioning:** M0 = v0.0.1 intern. v0.1 nach Feature-Completeness.

**Tax-/Lizenz-Fragen:** Open-Source unter MIT, später entscheiden ob Dual-License für Enterprise-Features.

---

## Startbedingung

M0 startet sinnvoll wenn:
- Bernhard hat QSP Tax-Advisor-Review durch
- ProxyPass hat ersten zahlenden Kunden (nicht nur Contacter)
- Repo ist angelegt + CI-Template steht
- Ein Basis-Test-Projekt existiert (Fixture-Setup)

**Pre-M0-Todo:**
- Repo aufsetzen (Mono-Repo Struktur)
- Razorshave.Cli, Razorshave.Abstractions, Razorshave.Runtime als Skeleton-Projekte
- Test-Setup (xUnit + Verify + Vitest + Playwright)
- Dieses Doc reviewen und bei Bedarf anpassen
