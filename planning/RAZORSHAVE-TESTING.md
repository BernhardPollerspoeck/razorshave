# Razorshave: Test-Strategie

> Wie Razorshave getestet wird — differenziert pro Layer, mit Coverage-Zielen, TDD wo sinnvoll, Playwright für E2E.

**Status:** Design-Draft
**Parent:** RAZORSHAVE.md
**Datum:** 2026-04-19

---

## Kern-Prinzip

Razorshave hat **mehrere unabhängige Komponenten** mit unterschiedlichen Test-Anforderungen. Pauschale 95%-Coverage für alles ist Scheingenauigkeit. Differenzierte Ziele pro Layer machen die Tests ehrlich und erreichbar.

**Hohe Coverage für pure Logic, funktionale Tests für Integration, dokumentierter Smoke-Test für MSBuild-Task.**

---

## Coverage-Ziele pro Layer

| Layer | Ziel | Methode | Tool |
|---|---|---|---|
| Transpiler | 95%+ | TDD + Snapshot-Tests | xUnit + Verify |
| Analyzer | 95%+ | TDD + Diagnostic-Tests | xUnit + Microsoft.CodeAnalysis.Testing |
| Source-Generator | 95%+ | TDD + Snapshot-Tests | xUnit + Verify |
| Runtime (Pure Logic) | 90%+ | TDD | Vitest |
| Runtime (DOM/Browser) | ~70% | Integration-Tests | Playwright |
| MSBuild-Task | ~60% | Manueller Smoke-Test-Katalog | Dokumentation |
| End-to-End | Funktional | Playwright gegen Kitchen-Sink-App | Playwright |

---

## Layer-Details

### 1. Transpiler (TDD, 95%+)

Das Herzstück. Jede C#→JS-Transformation muss getestet sein.

**Test-Pattern:**

Fixture-Files mit Input (`.razor` oder `.cs`) und Expected-Output (`.verified.js`):

```
tests/transpiler/fixtures/
├── basics/
│   ├── simple-counter/
│   │   ├── Input.razor
│   │   └── Output.verified.js
│   ├── foreach-list/
│   ├── async-lifecycle/
│   └── operator-overload-money/
├── expressions/
│   ├── pattern-matching/
│   ├── string-interpolation/
│   └── linq-chains/
├── control-flow/
│   ├── if-else/
│   ├── switch-expression/
│   └── try-catch-finally/
└── render-tree/
    ├── simple-markup/
    ├── nested-components/
    └── event-handlers/
```

**Test-Flow:**

1. Fixture-Folder anlegen
2. Test-Case schreiben: "Take Input.razor, transpile, verify against Output.verified.js"
3. Erstes Mal: Output.verified.js fehlt → Verify generiert Output.received.js
4. Developer reviewed den Output, akzeptiert wenn richtig (wird committed als .verified.js)
5. Spätere Änderungen: Verify zeigt Diff, Developer akzeptiert oder fixed

**Vorteil:** Snapshot-Tests lassen sich massiv per Diff reviewen. PR-Reviews sehen genau welcher JS-Output sich durch eine Transpiler-Änderung ändert.

**TDD-Workflow:**
- Neue Feature: Zuerst Fixture + erwartetes Output-Konzept
- Test schreiben, läuft rot
- Transpiler erweitern bis grün
- Output-Snapshot akzeptieren
- Nächster Test

### 2. Analyzer (TDD, 95%+)

Prüft ob User-Code Razorshave-regeln einhält, gibt Diagnostics.

**Test-Pattern** (Microsoft.CodeAnalysis.Testing):

```csharp
[Fact]
public async Task DbContextInjection_ReportsRZS1001() {
    var code = @"
        @inject {|RZS1001:DbContext|} Db
        <p>Hello</p>";
    
    await new AnalyzerTest<RazorshaveAnalyzer> {
        TestCode = code,
        ExpectedDiagnostics = {
            new DiagnosticResult("RZS1001", DiagnosticSeverity.Error)
                .WithSpan(2, 18, 2, 27)
                .WithMessage("Symbol 'Microsoft.EntityFrameworkCore.DbContext' is not in the Razorshave ecosystem.")
        }
    }.RunAsync();
}
```

**Coverage pro Diagnostic-Code** (RZS1001 bis RZSxxx):
- Jeder RZS-Code hat mindestens einen "positive" Test (Fehler wird erkannt)
- Jeder hat einen "negative" Test (kein falscher Positiv bei legitimate Code)

### 3. Source-Generator (TDD, 95%+)

Generiert `[ApiRoute]`-Client-Impls. Reine Transformation.

**Test-Pattern** (wieder Verify):

```csharp
[Fact]
public async Task UserApi_GeneratesCorrectClient() {
    var source = @"
        [ApiRoute(""api/users"")]
        public interface IUserApi {
            [Get] Task<User[]> GetAllAsync();
            [Get(""{id}"")] Task<User> GetByIdAsync(int id);
        }";
    
    var generated = RunGenerator(source);
    
    await Verify(generated);  // Snapshot-Test
}
```

### 4. Runtime Pure Logic (TDD, 90%+)

JavaScript-Tests mit Vitest. Testet Komponenten der Runtime die **ohne Browser** laufen.

**Was pure Logic ist:**

- VDOM-Diff-Algorithmus (Input: two VNodes, Output: Patch-Instructions)
- Store-Class (get/set/delete/batch/onChange)
- Route-Matching (URL pattern → parameters)
- DI-Container (register/resolve/singleton-caching)
- EventArgs-Wrapper (native Event → wrapped)

**Test-Pattern:**

```js
// runtime/store.test.js
import { describe, it, expect, vi } from 'vitest';
import { Store } from './store.js';

describe('Store', () => {
    it('notifies on set', () => {
        const store = new Store();
        const listener = vi.fn();
        store.onChange(listener);
        
        store.set('key1', { id: 1 });
        
        expect(listener).toHaveBeenCalledOnce();
    });
    
    it('batches notifications', () => {
        const store = new Store();
        const listener = vi.fn();
        store.onChange(listener);
        
        store.batch(() => {
            store.set('a', 1);
            store.set('b', 2);
            store.set('c', 3);
        });
        
        expect(listener).toHaveBeenCalledOnce();  // nur 1x, nicht 3x
    });
});
```

### 5. Runtime DOM/Browser (Playwright, ~70%)

Was nur im Browser testbar ist: Rendering, DOM-Manipulation, History-API, fetch().

**Test-Pattern** — Playwright testet gegen eine Test-App die bewusst Features triggert:

```csharp
[Fact]
public async Task Counter_IncrementsOnClick() {
    await Page.GotoAsync("http://localhost:5173/counter");
    
    await Page.ClickAsync("button.increment");
    await Page.ClickAsync("button.increment");
    
    var text = await Page.TextContentAsync("[data-testid=count]");
    Assert.Equal("Count: 2", text);
}
```

Browser-Features die getestet werden müssen:
- Component-Mount/Unmount (DOM-Änderungen)
- Re-Rendering nach State-Update
- Keyed List-Updates (Items werden korrekt gemoved statt recreated)
- Event-Handling mit allen EventArgs-Typen
- Router-Navigation (pushState, popState, Back-Button)
- Scoped CSS funktioniert (Styles bleiben isoliert)
- CascadingValue propagiert durch Component-Tree
- Store-Subscription triggert Re-Render

### 6. MSBuild-Task (Manuell + Dokumentiert)

Der Task ist **Orchestrierung** — ruft nur andere (getestete) Komponenten auf. Kein Kandidat für 95% Unit-Test-Coverage.

**Ansatz:** Smoke-Test-Katalog, bei Release manuell durchspielen.

**Katalog (in `/tests/msbuild/scenarios.md` dokumentiert):**

| # | Szenario | Erwartung |
|---|---|---|
| 1 | Simple Counter-App bauen | dist/ mit app.js, app.css, index.html |
| 2 | App mit `@inject DbContext` bauen | Build fails mit RZS1001 |
| 3 | App mit ProjectReference auf Contracts | Beide Projekte transpiliert |
| 4 | Multi-Target-Framework (net8.0;net9.0) | Config wählt korrektes Target |
| 5 | `[ApiRoute]`-Interface vorhanden | Source-Gen erzeugt Client, wird transpiliert |
| 6 | Clean-Build (obj/ leer) | MSBuild restored, Razorshave läuft |
| 7 | Incremental-Build (unverändert) | Output identisch, schnell |
| 8 | C#-Syntax-Fehler im User-Code | MSBuild fails früh, Razorshave läuft nicht |
| 9 | wwwroot/ mit Assets | Kopie nach dist/assets/ 1:1 |
| 10 | Scoped CSS mit ::deep | CSS korrekt gebundled und gescopet |
| 11 | dotnet build ohne -c Razorshave | Razorshave läuft nicht (normale Blazor-Server-Build) |
| 12 | dotnet build -c Razorshave | Razorshave-Target triggert automatisch |

Nach jedem Release: Katalog durchspielen, Ergebnisse pro Szenario notieren.

**v0.2-Ziel:** Katalog als automatisierte Integration-Tests implementieren.

### 7. End-to-End (Playwright, "Kitchen Sink" App)

Eine einzige **Razorshave-Kitchen-Sink-App** die alle Features zeigt. Dient gleichzeitig als:

- **Automatisierter E2E-Test** (Playwright fährt durch alle Pages)
- **Live-Demo** für die Website (`demo.razorshave.dev`)
- **Dokumentations-Referenz** (jede Page hat Source-Link zu GitHub)
- **Bundle-Size-Benchmark** (realistische Razorshave-App-Metrik)

**Page-Struktur:**

```
/                           → Landing mit Feature-Liste + Links
/counter                    → Basic State, Events
/todos                      → List-Rendering, @key, @foreach
/routing                    → NavigationManager, Links, Route-Constraints  
/routing/{id:int}/{action?} → Komplexe Routes mit Parametern
/forms                      → @bind, Input-Handling
/api-demo                   → ApiClient (Get/Post/Put/Delete/Retry/Timeout/Cancel/FormData)
/store-demo                 → Store mit Cross-Component-State, Batch, OnChange
/lifecycle                  → Alle Lifecycle-Hooks mit Console-Output
/css-scoping                → Scoped CSS mit ::deep
/js-interop                 → IJSRuntime, Chart.js-Integration
/cascading-values           → CascadingValue für Theme-Provider
/error-boundary             → Error-Handling bei Component-Crash
/decimal-math               → decimal.js-light in Action
/operator-overload          → Money-Class mit Operator-Overloading
/events-all                 → Jeden EventArgs-Typ einmal testen
/location-changing          → Navigation-Blocker bei Unsaved-Changes
/multiple-contracts         → App mit Contracts-Project-Reference
```

**Playwright-Tests (pro Page):**

Jede Page hat mindestens einen Smoke-Test:
- Navigation zur Page funktioniert
- Page rendert ohne Fehler
- Feature-spezifische Interaktion funktioniert (Click, Type, Submit, etc.)
- State-Änderungen reflektieren im DOM

Einige Pages haben ausführlichere Tests:
- `/api-demo`: Alle HTTP-Features mit Mock-Server
- `/store-demo`: Batch-Update-Verhalten verifizieren
- `/routing`: Alle Route-Constraint-Typen parsen korrekt

**CI-Integration:** Bei jedem PR wird Kitchen-Sink gebaut, deployed auf Ephemeral-Environment, Playwright-Tests laufen gegen deployed App.

---

## Test-Projekt-Struktur

```
tests/
├── transpiler/
│   ├── Razorshave.Transpiler.Tests.csproj
│   ├── fixtures/                    ← Input + verified.js Output
│   └── [TestCategory]Tests.cs
├── analyzer/
│   ├── Razorshave.Analyzer.Tests.csproj
│   └── [DiagnosticCode]Tests.cs
├── source-generator/
│   ├── Razorshave.SourceGenerator.Tests.csproj
│   └── [Feature]Tests.cs
├── runtime/
│   ├── vitest.config.js
│   ├── package.json
│   └── src/                         ← .test.js files neben den Runtime-Files
├── msbuild/
│   └── scenarios.md                 ← Manueller Smoke-Test-Katalog
└── e2e/
    ├── kitchen-sink/                ← Die Test-App
    │   ├── Razorshave.KitchenSink.Client.csproj
    │   └── Pages/
    ├── kitchen-sink-server/         ← Mock-API-Server für Tests
    └── playwright-tests/
        ├── Razorshave.E2ETests.csproj
        └── [Page]Tests.cs
```

---

## Coverage-Tools

**.NET (Transpiler, Analyzer, Source-Generator, MSBuild-Task):**
- Coverlet (Collector) für Coverage-Messung
- ReportGenerator für HTML-Reports
- Läuft via `dotnet test --collect:"XPlat Code Coverage"`

**JavaScript (Runtime):**
- Vitest eingebauter Coverage (c8-basiert)
- `vitest run --coverage`

**Kombinierte Reports:**
- In CI: beide Reports werden generiert, als Artifacts hochgeladen
- PR-Kommentare zeigen Coverage-Delta (Codecov oder SonarCloud)

---

## CI-Gates

Pro Layer werden Coverage-Threshold erzwungen:

```yaml
# CI-Pipeline (Pseudo)
jobs:
  test-transpiler:
    coverage_min: 95
    on_fail: block_pr
  
  test-analyzer:
    coverage_min: 95
    on_fail: block_pr
  
  test-source-generator:
    coverage_min: 95
    on_fail: block_pr
  
  test-runtime-pure:
    coverage_min: 90
    on_fail: block_pr
  
  test-runtime-dom:
    playwright_all_pass: true
    on_fail: block_pr
  
  test-e2e:
    playwright_all_pass: true
    on_fail: block_pr
```

**MSBuild-Smoke-Test** ist nicht im CI (manuell vor Release). Wird in Release-Checklist dokumentiert.

---

## TDD-Praxis (wo streng, wo pragmatisch)

**Strikt TDD:**
- Transpiler (jedes C#-Konstrukt bekommt zuerst einen Test)
- Analyzer (jeder RZS-Code entsteht durch einen failing Test)
- Source-Generator
- Runtime-Pure-Logic

**Pragmatisch (Test-After ist ok):**
- Runtime-DOM-Code (Browser-Setup macht TDD umständlich)
- Playwright-Tests (meist nach Feature-Implementation geschrieben)

**Nur Smoke-Test:**
- MSBuild-Task

---

## Offene Punkte

- **Playwright vs. andere Tools:** Playwright ist gesetzt (Microsoft, .NET-Support). Keine Alternativen.
- **Mock-Server für ApiClient-Tests:** MSW (Mock Service Worker) oder eigener Test-Server? Entscheidung während Implementation.
- **Coverage-Tool-Integration:** Codecov vs. SonarCloud vs. pure Coverlet-Reports. Entscheidung wenn CI-Pipeline steht.
- **Test-Execution-Time-Budget:** CI sollte unter X Minuten laufen. Zahl offen bis wir's messen.
