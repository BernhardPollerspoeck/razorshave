# Razorshave: Bootstrap-Plan

> Wie wir anfangen. Vom leeren Repo bis zum ersten "Counter läuft im Browser" Moment.

**Status:** Design-Draft
**Parent:** RAZORSHAVE.md
**Datum:** 2026-04-19

---

## Übersicht

Der Weg zum M0-Ziel ("`dotnet new blazor` transpiliert läuft im Browser"). Setup (Repo, Skeleton-Projekte, CI, Kitchen-Sink) ist erledigt — verbleibend ist der Implementation-Block (Schritt 5) und die Do-or-Don't-Entscheidung (Schritt 6).

---

## Schritt 5: Erste echter Code (der große Block)

Das ist wo die echte Arbeit anfängt. In 14 Sub-Schritten.

### 5.1: Roslyn-Exploration

Bevor wir transpilieren: verstehen was Roslyn uns gibt.

Scratch-Test in `tests/transpiler/`: einfach eine `.razor.g.cs`-Datei einlesen und den SyntaxTree ausgeben. Was steht da drin, wie sind die Nodes strukturiert, was sagt SemanticModel über Types und Symbols.

Output: Wissen wie der Roslyn-API tickt. Keine Production-Code, nur Lernzeit.

Aufwand: 1-2 Abende.

### 5.2: Fixture-Setup

Die erste echte Test-Fixture: `tests/transpiler/fixtures/counter/`.

Wir nehmen die Counter.razor aus unserem Blazor-Template, lassen `dotnet build` drauf laufen, und kopieren den generierten `Counter.razor.g.cs` als `Input.g.cs` ins Fixture. Das ist ab jetzt unser **Referenz-Input** den wir transpilieren wollen.

Output: stabile Test-Fixture für alle folgenden Sub-Schritte.

Aufwand: 1 Abend.

### 5.3: Erster Walker — Klassen-Skelett

Transpiler-Walker der NUR die Klassen-Deklaration erkennt und eine leere JS-Class emittiert:

```js
export class Counter extends Component {}
```

Keine Body, keine Methoden. Nur das Skelett.

Test: "Counter.razor.g.cs rein, leere JS-Class raus." Snapshot-Test passt.

Aufwand: 2-3 Abende.

### 5.4: Fields und Properties

Walker erweitern: erkennt `private int currentCount = 0` → emittiert `currentCount = 0;` in der JS-Class.

Property-Access innerhalb Methoden-Bodies wird als `this.currentCount` emittiert.

Aufwand: 2-3 Abende.

### 5.5: Methoden

Walker erweitern: erkennt Method-Declarations → emittiert JS-Methoden.

Erst ohne Body. Dann mit simplen Statements (Assignment, `++`, `+= 1`). Minimal-Set für Counter.

Aufwand: 3-4 Abende.

### 5.6: RenderTreeBuilder-Walker (parallel zu 5.5 möglich)

Das ist der zweite separate Walker. Erkennt die `__builder.OpenElement()/AddAttribute()/AddContent()/CloseElement()`-Calls in `BuildRenderTree` und emittiert `h(...)`-Calls.

Start mit einer einzigen `<button>`:

```js
// Input: __builder.OpenElement(0, "button"); 
//        __builder.AddContent(1, "Count: "); 
//        __builder.AddContent(2, this.currentCount); 
//        __builder.CloseElement();

// Output:
h('button', {}, 'Count: ', this.currentCount)
```

Aufwand: 3-5 Abende.

### 5.7: Event-Handler-Wrapping

Walker 5.6 erweitern: erkennt `EventCallback.Factory.Create<MouseEventArgs>(this, IncrementCount)` im BuildRenderTree → emittiert `{ onclick: (e) => this.incrementCount() }` als Props.

Aufwand: 2-3 Abende.

### 5.8: Integration — erster lauffähiger Transpiler

Alle Walker zusammenführen. `Counter.razor.g.cs` rein, vollständiges `Counter.js` raus. Snapshot committed.

Output: **erster kompletter transpilierter Code einer echten Component.**

Aufwand: 2-3 Abende (Debugging der Integration).

### 5.9: Runtime-Skeleton (parallel zu 5.3-5.8 möglich)

Das JS-Framework drumrum. In `src/Razorshave.Runtime/` eine minimale Runtime:

- `Component`-Class mit `stateHasChanged()` + rAF-Scheduling
- `h()`-Function die VDOM-Objekte baut (`{ type, props, children }`)
- `diff()` naiv: kompletter Sub-Tree wird ersetzt bei jedem Update (kein keyed diff in diesem Schritt)
- `mount(RootComponent, domElement)` als Entry-Point
- Event-Handler-Auto-Rerender nach Handler-Ausführung

Tests mit Vitest parallel.

Aufwand: 1-2 Wochen.

### 5.10: End-to-End Mini

Counter-Transpile + Runtime zusammenführen.

HTML-File mit `<div id="app"></div>` und Script-Tag der:

```js
import { mount } from '@razorshave/runtime';
import { Counter } from './Counter.js';

mount(Counter, document.getElementById('app'));
```

Im Browser öffnen → Counter erscheint → Click → Zahl erhöht sich.

**Das ist der Magic-Moment.** Ab hier ist "geht überhaupt" beantwortet.

Aufwand: 2-4 Abende Integration + Debugging.

### 5.11: Weather.razor

Counter läuft. Jetzt Weather.razor als zweite Fixture. Bringt neue Features:

- `OnInitializedAsync` Lifecycle
- `@inject` mit IWeatherApi
- `foreach` über Array
- Async-Load + Re-Render
- ApiClient-Minimal im Runtime (nur `Get<T>`)
- DI-Container-Minimal im Runtime (Singleton-Resolve)

Zwei neue Transpiler-Features: async/await-Mapping, foreach-über-Array.

Aufwand: 2-3 Wochen.

### 5.12: Router

Zwei Pages (Counter + Weather + Home) existieren. Jetzt Router bauen.

- `@page`-Attribute aus Components sammeln → build-time Route-Table
- Runtime-Router mit URL-Matching (simple paths, keine Constraints)
- pushState / popState Handling
- NavigationManager-Runtime-Class
- `<NavLink>` Component in Runtime mit active-class

Aufwand: 2-3 Wochen.

### 5.13: MSBuild-Integration

Bis hier haben wir Transpiler als Standalone-CLI getestet. Jetzt als MSBuild-Task wrappen.

- `Razorshave.Cli.targets` File das MSBuild automatisch lädt
- `[RazorshaveTranspileTask]` Class die als MSBuild-Task läuft
- esbuild-Binary als embedded Resource, wird ins Temp-Dir extrahiert
- Volle Pipeline: `dotnet build -c Razorshave` → Razorshave-Task → dist/

Aufwand: 1-2 Wochen.

### 5.14: M0-Complete

Alle 15 Definition-of-Done-Punkte durchgehen. Fixen was nicht geht.

Ehrliche Checkliste mit Bernhard als Human-Tester gegen die Kitchen-Sink-Demo.

Aufwand: 1-2 Wochen Debugging.

---

## Schritt 6: Do-or-Don't

M0-Entscheidung treffen. Siehe `RAZORSHAVE-M0.md` für Kriterien.

---

## Gesamt-Aufwand

Bei Abend-Arbeit (4-8h/Woche fokussierte Zeit):

| Phase | Wandzeit |
|---|---|
| Schritt 5.1-5.2 (Exploration, Fixture) | 2-3 Wochen |
| Schritt 5.3-5.8 (Transpiler-Core für Counter) | 2-3 Monate |
| Schritt 5.9 (Runtime-Skeleton) | 3-4 Wochen |
| Schritt 5.10 (End-to-End Mini) | 1-2 Wochen |
| Schritt 5.11-5.12 (Weather + Router) | 2-3 Monate |
| Schritt 5.13 (MSBuild) | 3-4 Wochen |
| Schritt 5.14 (M0-Complete) | 2-4 Wochen |
| **Summe** | **~9-13 Monate Wandzeit** |

Das ist ehrliche Schätzung. Kann schneller gehen bei konzentrierten Phasen, kann länger dauern bei Edge-Cases.

**Wichtig:** Schritt 5.9 (Runtime) und 5.3-5.8 (Transpiler) können **parallel** laufen. Zwei unabhängige Arbeitsstränge. Wenn du an einem Abend keine Lust auf Transpiler-Syntax-Tree-Walken hast, machst du halt Runtime-VDOM-Diffing.

---

## Pre-M0-Checkliste

Bevor Schritt 5.3 losgeht, muss stehen:

- [ ] Fixture-Setup für Counter vorbereitet
- [ ] Roslyn-API verstanden (Schritt 5.1 durchlaufen)
- [ ] Dieses Doc nochmal reviewen, ggf. anpassen

---

## Post-M0

Nach erfolgreichem M0 und "Continue"-Entscheidung:

- Diagnostics-Katalog komplett (alle RZS-Codes)
- Source-Generator für `[ApiRoute]`
- Keyed-VDOM-Diff (Preact-Level)
- Volle EventArgs-Liste
- Store + IJSRuntime + decimal.js-light
- Route-Constraints
- LocationChangingHandler
- Kitchen-Sink auf alle Features erweitern
- Playwright-E2E-Tests
- Docs-Website
- Public Release als v0.1

Das ist v0.1-Scope, wird in separatem Roadmap-Doc ausgearbeitet wenn M0 durch ist.
