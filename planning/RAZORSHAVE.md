# Razorshave

> Write Blazor. Deploy as SPA.

.NET SPA-Framework das Blazor-Syntax (Razor + C#) zu nativem JavaScript/HTML/CSS transpiliert. Kein WASM-Runtime, kein SignalR, kein Server im Production-Deployment. Output ist ein statisches Bundle, deploybar auf jedem Webserver (nginx, S3, Cloudflare Pages).

**Status:** Konzeption / PRD-Entwurf
**Owner:** Bernhard Pollerspöck (QSP GmbH)
**Datum:** 2026-04-19

---

## Pitch

Nimm ein minimales Blazor-Projekt (`dotnet new blazor`), folge drei Regeln, kompiliere mit `razorshave build`. Output: statisches SPA-Bundle, ~150-300 KB, nginx-deploybar.

**Zielgruppe:** .NET-Shops die interne Tools, Admin-Panels, Dashboards als SPA wollen aber keinen Node.js/React-Stack einführen mögen. Besonders Enterprise und kleine .NET-fokussierte Teams.

**Nicht-Ziel:** SEO-kritische Public-Marketing-Pages, komplexe Server-Side-Rendering-Szenarien, Auth-heavy Applications mit klassischem Cookie-Flow.

---

## Kern-Ansatz

**Dev-Experience:** Unverändertes Blazor Server. Hot Reload, Debugger, IntelliSense, Razor-IDE-Support — alles von Microsoft, null Eigenaufwand.

**Build-Experience:** Eigenes CLI `razorshave build`. Analysiert Projekt, transpiliert zu JS, bundelt, wirft `dist/` raus.

**Runtime-Experience:** Mini-Runtime (~100-200 KB) mit VDOM, Client-Router, Signal-Reactivity, Fetch-Wrapper. Kein .NET im Browser.

### Warum das funktioniert

Der Razor-Compiler erzeugt aus `.razor`-Files bereits C#-Code mit `BuildRenderTree(RenderTreeBuilder)`. Das ist **im Kern schon VDOM-Code**. Razorshave hängt sich an diesen Compiler-Output an und muss nur noch C# → JS transpilieren und RenderTreeBuilder-Calls zu VDOM-Ops mappen.

**Wir transpilieren nicht Razor. Wir transpilieren was Razor zu C# macht.**

---

## Flow

1. **Analyse / Validation**
   - Roslyn lädt Projekt
   - Alle `@page` Components → Route-Table
   - Alle `@inject` → Allowlist-Check (Built-ins + `[Client]` markierte)
   - Verwendete Types/Methods → BCL-Subset-Allowlist
   - Server-Only Attribute (`[Authorize]`, `[StreamRendering]`) → Violations
   - Output: Violation-Report mit Filename/Line/Message
   - Fail wenn Violations gefunden

2. **Transpilation**
   - Razor-Compiler läuft regulär (erzeugt C# aus `.razor`)
   - C# → JS Transpiler auf generiertem Code
   - CSS-Isolation-Outputs einsammeln (scoped Selectors)
   - `[Client]` Services transpilieren
   - ApiClient-Interfaces zu Fetch-Clients generieren

3. **Bundling & Output**
   - Runtime-Module einschließen
   - Tree-Shaking / DCE (via esbuild/rollup)
   - Minification
   - Route-Manifest generieren
   - `index.html` mit Entry-Script erzeugen
   - Assets (CSS, Images, wwwroot-Content) kopieren
   - Final output: `dist/`

---

## Regeln für User-Projekte

1. **Keine Auth-Components.** Auth läuft über API (JWT/OAuth), nicht über Blazor Identity / Cookie-Auth.
2. **Services nur mit `[Client]` Attribute injizierbar.** Oder Convention: alles in `/Client` Ordner.
3. **Keine SSR-abhängigen APIs.** Kein `PersistentComponentState`, kein `StreamRendering`, kein `OnAfterRender.firstRender` mit SSR-Semantik.

Wer bricht → Build-Fail mit klarer Message.

### Ignorierbar (keine Regel nötig)

- `@rendermode InteractiveServer` / `InteractiveAuto` / `InteractiveWebAssembly` — Transpiler liest Compiler-Output, Direktive ist eh weg-abstrahiert
- `AddInteractiveServerComponents()` in Program.cs — Program.cs wird beim Build ignoriert
- SignalR-Infrastruktur — existiert im Dev, irrelevant im Build

**Das User-Projekt läuft unverändert als Blazor Server im Dev.**

---

## Architektur

### Transpiler (Core)

Roslyn-basiert. Input: generierter C#-Code aus Razor-Compiler. Output: JavaScript-Module.

**Mapping-Strategie:**
- Classes → ES6 Classes
- async/await → native JS async/await
- LINQ → Array-Methods (`.Where` → `.filter`, `.Select` → `.map`) oder Runtime-Shims
- Generics → erased (wie TypeScript)
- Nullable, Pattern Matching, Records → JS-Äquivalente
- RenderTreeBuilder-Calls → VDOM-Ops

**Nicht unterstützt (Compile-Error):**
- `unsafe`, Pointer
- `Span<T>` mit Memory-Layout
- `ref struct`
- Reflection (außer trivial)
- `dynamic`
- Komplexe Generics-Resolution zur Laufzeit
- Multithreading (kein Thread, kein ThreadPool)

### BCL-Subset (Shims)

Minimale JavaScript-Shims für:
- `System.Collections.Generic` (List, Dictionary, HashSet, Queue, Stack)
- `System.Linq` (Basics: Where, Select, ToList, First/FirstOrDefault, Count, Any, All, OrderBy, GroupBy)
- `System.String` Operations
- `DateTime`, `TimeSpan`
- `Task` / `Task<T>` → Promise-Bridge
- `Guid`

Alles außerhalb: Compile-Error mit klarer Message.

### Runtime (Mini-Framework)

Ziel: ~100-200 KB gzipped.

- VDOM + Diff-Algorithmus
- Client Router (History API, matched `@page` Routes)
- Signal-basierte Reactivity (`StateHasChanged()` → Signal-Update)
- Component-Lifecycle (`OnInitialized`, `OnParametersSet`, etc.)
- EventCallback-System
- CascadingValue-Support
- `HttpClient`-Shim (fetch-Wrapper mit Auth-Header-Injection)
- `NavigationManager`-Shim
- `IJSRuntime`-Shim (direktes JS-Interop)

### ApiClient-Pattern

Abstract base class oder Interface `IApiClient` / `ApiClient`. User-Code erbt davon:

- Dev: konkrete Impl mit direktem Service-Zugriff (Blazor Server nutzt Server-Services)
- Build: generierte Fetch-Client-Impl mit HTTP-Calls gegen Backend-API

Business-Logik bleibt server-side. Client ruft nur API-Endpoints.

### Build-CLI

`razorshave` als Global Tool (`dotnet tool install -g Razorshave.Cli`)

Commands:
- `razorshave build` — Production-Build nach `dist/`
- `razorshave analyze` — Nur Validation, kein Output (für CI)
- `razorshave init` — Fügt Razorshave-Konfiguration zu bestehendem Blazor-Projekt hinzu

Konfiguration via `razorshave.json` im Projekt-Root:
- Output-Pfad
- Entry-Component
- Minification-Settings
- BCL-Allowlist-Extensions (custom Shims)

---

## MVP Scope (v0.1)

Funktional gedacht als "`dotnet new blazor` (minimal) → `razorshave build` → `dist/` → nginx → läuft".

Transpiler-Coverage:
- Fields, Properties, Methods, Constructors
- Event-Handler (Lambdas, Method-Refs)
- `async`/`await` auf HTTP
- `foreach` über `IEnumerable`
- `@code`-Block, `@page`, `@inject`, `@rendermode` (ignoriert)
- `if/else`, `switch`, Pattern Matching (Basics)
- String-Interpolation
- Basic LINQ (Where, Select, ToList, Count, Any)

Components-Coverage:
- RenderFragment-Basics
- EventCallback
- Two-Way Binding (`@bind`)
- `NavLink`, `@Body`, Layout-System
- Basic `IJSRuntime` Interop

Nicht in v0.1:
- Auth
- EditForm + Validation
- Streaming Rendering
- PersistentComponentState
- Komplexe JS Interop
- SignalR Hub Proxies

---

## Milestones

**M0 — Proof of Concept (2-4 Wochen)**
Eine `Counter.razor` manuell transpiliert + Mini-Runtime. Button klickt, State updated, DOM reagiert.

**M1 — Minimales Blazor-Template funktioniert (3-4 Monate)**
Unmodifiziertes `dotnet new blazor` (minimal) baut erfolgreich nach `dist/` und läuft in nginx.

**M2 — Validator + Error-Reporting (1-2 Monate)**
Klare Compile-Errors bei Violations, inklusive Filename/Line/Message.

**M3 — CLI + MSBuild-Integration (1-2 Monate)**
`razorshave build` als Global Tool, plus MSBuild-Target für CI.

**M4 — Erste Drittprodukt-Anwendung (parallel)**
Dogfooding: ein QSP-Produkt-Dashboard (z.B. für ProxyPass Admin) mit Razorshave gebaut.

**Realistisch:** v0.1 Release in 8-14 Monaten Teilzeit.

---

## Schlachtplan: Transpiler

Das Herzstück. Hier wird aus C# JavaScript. Liste aller Dinge die erkannt (Analyse) und übersetzt (Mapping) werden müssen, gegliedert nach Komplexität.

### Legende
- ✅ = trivial, direktes 1:1 Mapping möglich
- ⚠️ = erfordert Design-Entscheidung oder Runtime-Support
- 🔥 = komplex, eigenes Subsystem nötig
- ❌ = nicht unterstützt, Compile-Error

---

### 1. Types & Struktur

| C# Konstrukt | JS-Mapping | Komplexität |
|---|---|---|
| `class` | ES6 class | ✅ |
| `partial class` | merge zu einer class | ✅ |
| `abstract class` | ES6 class (Laufzeit-Check auf Instantiierung) | ✅ |
| `sealed class` | ES6 class (Hinweis im Comment, kein Runtime-Check) | ✅ |
| `static class` | Objekt-Literal mit Funktionen | ✅ |
| `interface` | erased (TypeScript-Style) | ✅ |
| `struct` | ES6 class (wie Class behandelt, keine Value-Semantik) | ✅ |
| `record` | ES6 class (wie Class behandelt, keine Auto-Equality) | ✅ |
| `record struct` | wie record | ✅ |
| `enum` | Object.freeze({...}) oder const-Enum | ✅ |
| `[Flags] enum` | Bit-Ops bleiben erhalten, JS kann das | ✅ |
| `delegate` | Function-Type (erased) | ✅ |
| Inheritance (single) | `extends` | ✅ |
| Multi-Interface-Implementation | erased | ✅ |
| `ref struct` | Compile-Error | ❌ |
| `unsafe`, Pointer | Compile-Error | ❌ |

### 2. Members

| C# Konstrukt | JS-Mapping | Komplexität |
|---|---|---|
| Field | class field | ✅ |
| Auto-Property | class field | ✅ |
| Property mit Body | getter/setter | ✅ |
| Init-only Property | Convention (Frozen nach ctor) | ⚠️ |
| Method | class method | ✅ |
| Static Method | static method | ✅ |
| Constructor | constructor | ✅ |
| Multiple Constructors | Compile-Error (ein Ctor pro Klasse) | ❌ |
| Destructor/Finalizer | ignoriert mit Warning | ❌ |
| Indexer `this[]` | Compile-Error (v0.2+) | ❌ |
| Operator Overloading | Method-Rewrite: `a+b` → `T.op_Addition(a,b)` | ⚠️ |
| `readonly` Field | class field (JS hat kein readonly, Convention) | ✅ |
| `const` Field | class field mit primitiven Wert | ✅ |
| Events (`event`) | EventEmitter-Pattern oder Callback-Array | ⚠️ |

### 3. Expressions

| C# Konstrukt | JS-Mapping | Komplexität |
|---|---|---|
| Arithmetik `+ - * / %` | identisch | ✅ |
| Integer-Division | `Math.floor(a/b)` bei int-Typ-Kontext | ⚠️ |
| Boolean Ops `&& \|\| !` | identisch | ✅ |
| Vergleiche `== != < >` | `===` / `!==` | ✅ |
| Null-Coalescing `??` | `??` | ✅ |
| Null-Coalescing-Assignment `??=` | `??=` | ✅ |
| Conditional-Access `?.` | `?.` | ✅ |
| Ternary `? :` | ternary | ✅ |
| String-Interpolation `$"..."` | template literal | ✅ |
| `nameof(x)` | build-time String-Literal | ✅ |
| `typeof(T)` | Type-Referenz (bei uns erased) | ⚠️ |
| `is` Type-Check | `instanceof` (Klassen) / typeof (Primitive) | ⚠️ |
| `is` Pattern (`is int x`) | instanceof + Assignment | ⚠️ |
| `as` Cast | `instanceof ? x : null` | ✅ |
| Expliziter Cast `(int)x` | `Math.trunc(x)` bei numerischen Narrow-Casts | ⚠️ |
| `default(T)` | typed Default (0, null, false, ...) | ⚠️ |
| `new T()` | `new T()` | ✅ |
| `new T { Prop = v }` (Init-Syntax) | `Object.assign(new T(), {prop: v})` | ✅ |
| Collection-Initializer `new List<int>{1,2}` | `[1,2]` oder `new List([1,2])` | ✅ |
| Lambda `x => x+1` | arrow function | ✅ |
| Statement-Lambda `x => { ... }` | arrow function mit body | ✅ |
| `await` | `await` | ✅ |
| Tuple `(1, "a")` | Array oder Objekt `{Item1, Item2}` | ⚠️ |
| Tuple-Deconstruction `(a,b) = ...` | destructuring | ✅ |
| `with` Expression (records) | `{...obj, prop: v}` | ✅ |
| Range `..` `^1` | Array-Slice-Äquivalente | 🔥 |

### 4. Statements & Control Flow

| C# Konstrukt | JS-Mapping | Komplexität |
|---|---|---|
| `if/else` | identisch | ✅ |
| `while`, `do/while` | identisch | ✅ |
| `for` | identisch | ✅ |
| `foreach` über Array/List | `for...of` | ✅ |
| `foreach` über `IEnumerable<T>` | `for...of` (wenn iterable) | ⚠️ |
| `switch` Statement | switch | ✅ |
| `switch` Expression | Helper-Function mit if-chain | ⚠️ |
| Pattern Matching (basic) | if/else mit instanceof | ⚠️ |
| Pattern Matching (advanced, positional/property) | Helper-generiert | 🔥 |
| `try/catch/finally` | identisch | ✅ |
| `throw` | identisch | ✅ |
| `using var x = ...` (IDisposable) | try/finally mit `.dispose()` | ⚠️ |
| `yield return` | Generator-Function `function*` | ⚠️ |
| `goto` | Compile-Error | ❌ |
| `lock` | JS ist Single-Threaded | ❌ |

### 5. Nullability & Generics

| C# Konstrukt | JS-Mapping | Komplexität |
|---|---|---|
| `int?` / `T?` | erased | ✅ |
| `!` Null-Forgiving | entfernt | ✅ |
| Generic Class `Foo<T>` | class Foo (T erased) | ⚠️ |
| Generic Method `Bar<T>()` | function (T erased) | ⚠️ |
| Generic Constraints (`where T : class`) | erased | ✅ |
| `typeof(T)` in generic context | braucht Type-Token (generic erasure Problem) | 🔥 |
| `T t = default` | typed Default, wenn T bekannt | ⚠️ |
| Variance (`out T`, `in T`) | erased | ✅ |

### 6. Async

| C# Konstrukt | JS-Mapping | Komplexität |
|---|---|---|
| `async Task` | `async function` | ✅ |
| `async Task<T>` | `async function` | ✅ |
| `async ValueTask<T>` | `async function` | ✅ |
| `await t` | `await t` | ✅ |
| `Task.FromResult(x)` | `Promise.resolve(x)` | ✅ |
| `Task.Delay(ms)` | `new Promise(r => setTimeout(r, ms))` | ✅ |
| `Task.WhenAll` | `Promise.all` | ✅ |
| `Task.WhenAny` | `Promise.race` | ✅ |
| `CancellationToken` | AbortSignal (Runtime-Shim) | ⚠️ |
| `IAsyncEnumerable<T>` | Async Iterator | ⚠️ |

### 7. LINQ & Collections

Muss als BCL-Shim in der Runtime existieren. Zwei Optionen: (A) zu nativen JS-Array-Methods mappen (transparent), (B) eigene LINQ-Library im Runtime.

**Empfehlung v0:** (A) wo möglich, (B) als Fallback.

| LINQ Method | JS-Mapping | Komplexität |
|---|---|---|
| `.Where(p)` | `.filter(p)` | ✅ |
| `.Select(s)` | `.map(s)` | ✅ |
| `.SelectMany(s)` | `.flatMap(s)` | ✅ |
| `.ToList()` / `.ToArray()` | identity (ist schon Array) | ✅ |
| `.First()` | `.at(0)` mit Throw | ✅ |
| `.FirstOrDefault()` | `.at(0) ?? null` | ✅ |
| `.Single()` | length==1 Check + return | ✅ |
| `.Count()` | `.length` oder `.filter(p).length` | ✅ |
| `.Any()` | `.length>0` / `.some(p)` | ✅ |
| `.All(p)` | `.every(p)` | ✅ |
| `.Contains(x)` | `.includes(x)` | ✅ |
| `.OrderBy(k)` | `.toSorted((a,b) => k(a) - k(b))` | ✅ |
| `.OrderByDescending` | toSorted mit invertiertem Comparer | ✅ |
| `.ThenBy` | Composite Comparer | ⚠️ |
| `.GroupBy` | eigene Runtime-Impl | ⚠️ |
| `.Distinct()` | `[...new Set(arr)]` (nur primitive!) | ⚠️ |
| `.Sum()` / `.Min()` / `.Max()` / `.Average()` | `.reduce(...)` | ✅ |
| `.Take(n)` / `.Skip(n)` | `.slice(0,n)` / `.slice(n)` | ✅ |
| `.Zip` | eigene Impl | ⚠️ |
| `.Join` / `.GroupJoin` | eigene Impl | 🔥 |
| Deferred Execution | eager statt deferred | ⚠️ |

| Collection | JS-Mapping | Komplexität |
|---|---|---|
| `List<T>` | Array + Methoden-Shim | ✅ |
| `T[]` | Array | ✅ |
| `Dictionary<K,V>` | Map | ✅ |
| `HashSet<T>` | Set | ✅ |
| `Queue<T>` | Array + shift/push | ✅ |
| `Stack<T>` | Array + pop/push | ✅ |
| `IEnumerable<T>` | Iterable-Protocol | ✅ |
| `IList<T>` | Array | ✅ |
| `IReadOnlyList<T>` | Array | ✅ |
| `ReadOnlyCollection<T>` | Frozen Array | ✅ |

### 8. BCL-Primitives

| C# Type | JS-Mapping | Komplexität |
|---|---|---|
| `string` | string | ✅ |
| `int` | number | ✅ |
| `long` | number (kein BigInt in v0) | ✅ |
| `decimal` | Decimal (decimal.js-light, embedded) | ⚠️ |
| `double` / `float` | number | ✅ |
| `bool` | boolean | ✅ |
| `char` | 1-char string | ✅ |
| `byte` / `sbyte` / `short` / `ushort` | number | ✅ |
| `DateTime` | Date-Wrapper mit Kind-Handling | ⚠️ |
| `DateTimeOffset` | eigener Wrapper | ⚠️ |
| `TimeSpan` | Number (ms) oder Wrapper | ⚠️ |
| `Guid` | eigener Wrapper mit toString/parse | ⚠️ |
| `Uri` | URL (Built-in) | ✅ |

### 9. String-Operationen (häufig genutzt)

| C# Method | JS-Mapping | Komplexität |
|---|---|---|
| `string.Length` | `.length` | ✅ |
| `.Substring(i, n)` | `.substring(i, i+n)` | ✅ |
| `.IndexOf` / `.LastIndexOf` | identisch | ✅ |
| `.Contains` | `.includes` | ✅ |
| `.StartsWith` / `.EndsWith` | identisch | ✅ |
| `.Split` | identisch | ✅ |
| `.Replace` | identisch | ✅ |
| `.Trim` / `.TrimStart` / `.TrimEnd` | identisch | ✅ |
| `.ToUpper` / `.ToLower` | `toUpperCase` / `toLowerCase` | ✅ |
| `.ToUpperInvariant` | toUpperCase (JS ist eh invariant) | ✅ |
| `string.IsNullOrEmpty` | `!s` | ✅ |
| `string.IsNullOrWhiteSpace` | `!s?.trim()` | ✅ |
| `string.Join(sep, items)` | `items.join(sep)` | ✅ |
| `string.Format("{0}", x)` | template literal (build-time rewrite) | ⚠️ |
| `string.Concat(...)` | `+` | ✅ |
| `.PadLeft` / `.PadRight` | `.padStart` / `.padEnd` | ✅ |

### 10. Reflection & Attributes

| C# Konstrukt | JS-Mapping | Komplexität |
|---|---|---|
| Attributes am User-Code | `[Client]` erkennen, Rest ignorieren | ✅ |
| `[Parameter]`, `[CascadingParameter]` | Runtime nutzt für Component-Props | ⚠️ |
| `typeof(X)` | Class-Referenz | ⚠️ |
| `GetType()` | `.constructor` | ✅ |
| Reflection (PropertyInfo, MethodInfo etc.) | Compile-Error | ❌ |
| `dynamic` | Compile-Error | ❌ |
| Expression-Trees | Compile-Error | ❌ |

### 11. Blazor-spezifisch (RenderTreeBuilder)

Der ganze Block wird NICHT C#-zu-JS-transpiliert wie User-Code, sondern zu **hyperscript VDOM-Calls** gemappt. Das ist eine **separate Pipeline** im Transpiler.

| RenderTreeBuilder Call | VDOM-Mapping | Komplexität |
|---|---|---|
| `OpenElement(seq, "div")` / `CloseElement()` | Element-Node | ✅ |
| `OpenComponent<T>()` / `CloseComponent()` | Component-Node | ✅ |
| `AddContent(seq, text)` | Text-Node | ✅ |
| `AddContent(seq, expr)` | Expression-Node (reactive) | ⚠️ |
| `AddAttribute(seq, "class", v)` | Prop | ✅ |
| `AddAttribute(seq, "onclick", EventCallback.Factory.Create(...))` | Event-Handler-Binding | ⚠️ |
| `AddMarkupContent(seq, "raw html")` | dangerouslySetInnerHTML-Äquivalent | ⚠️ |
| `SetKey(k)` | key-Prop (wie React) | ✅ |
| `AddComponentReferenceCapture` | ref-Handling | ⚠️ |
| `AddElementReferenceCapture` | ref-Handling | ⚠️ |
| `OpenRegion` / `CloseRegion` | ignorierbar (ist für Diff-Optimierung) | ✅ |
| Sequence Numbers | ignorieren (nicht relevant für Client-VDOM) | ✅ |

### 12. Built-in Blazor Components (müssen in Runtime nachgebaut werden)

| Component | Runtime-Aufwand |
|---|---|
| `PageTitle` | trivial (document.title setzen) |
| `HeadContent` | trivial (head-Manipulation) |
| `NavLink` | einfach (active-class bei route-match) |
| `Router` | komplettes Client-Router-Subsystem 🔥 |
| `RouteView` | teil von Router |
| `CascadingValue<T>` | Context-ähnlich, mittlerer Aufwand |
| `ErrorBoundary` | mittel (componentDidCatch-Äquivalent) |
| `DynamicComponent` | mittel |
| `EditForm` + Validation | v0 NEIN ❌ |
| `InputText/Number/...` | v0 NEIN ❌ |

### 13. Component Lifecycle

| Blazor Method | JS-Mapping | Komplexität |
|---|---|---|
| `OnInitialized()` | `onInit()` Runtime-Hook | ✅ |
| `OnInitializedAsync()` | async `onInit()` | ✅ |
| `OnParametersSet()` | `onPropsChanged()` | ✅ |
| `OnParametersSetAsync()` | async dito | ✅ |
| `OnAfterRender(bool firstRender)` | `onAfterRender(firstRender)` | ✅ |
| `OnAfterRenderAsync` | async dito | ✅ |
| `ShouldRender()` | `shouldRender()` | ✅ |
| `StateHasChanged()` | auto-getriggert nach Events, manuell callable | ⚠️ |
| `Dispose()` / `IDisposable` | `onDestroy()` | ✅ |
| `IAsyncDisposable` | async `onDestroy()` | ✅ |

### 14. Dependency Injection

| C# Konstrukt | JS-Mapping | Komplexität |
|---|---|---|
| `@inject IService` | Constructor-Injection via Container-Lookup | ⚠️ |
| `[Inject]` Attribute | dito | ⚠️ |
| `IServiceProvider` | Container-Runtime | ⚠️ |
| Scoped Services | singleton im Client (es gibt keinen Request-Scope) | ⚠️ |
| Singleton Services | singleton | ✅ |
| Transient Services | neue Instance per Resolve | ✅ |
| Constructor-Injection in Services | Container nutzt ctor-params | ⚠️ |
| `[Client]` markierte Services | in Container registrieren | ✅ |
| Non-`[Client]` Services | Compile-Error bei Inject | ❌ |

### 15. HTTP & API

| C# Konstrukt | JS-Mapping | Komplexität |
|---|---|---|
| `HttpClient` | fetch-Wrapper-Class | ⚠️ |
| `HttpClient.GetFromJsonAsync<T>` | fetch + json() + typed return | ✅ |
| `HttpClient.PostAsJsonAsync` | fetch mit body JSON | ✅ |
| Custom `ApiClient` Basisklasse | Runtime-Basis mit Get/Post/Put/Delete | ✅ |
| Headers (Auth-Token) | fetch-Wrapper injiziert | ⚠️ |
| JSON (de)serialization | `JSON.parse/stringify` + Type-Hints | ⚠️ |

### 16. Event Handling

| Blazor Pattern | JS-Mapping | Komplexität |
|---|---|---|
| `@onclick="Handler"` | `onclick: bound(this.handler)` | ✅ |
| `@onclick="@(e => Handler(e))"` | inline arrow | ✅ |
| `@onclick:preventDefault` | preventDefault wrapper | ✅ |
| `@onclick:stopPropagation` | stopPropagation wrapper | ✅ |
| `EventCallback<T>` | Function-Type | ✅ |
| `EventCallback.Factory.Create` | direktes Function-Binding | ✅ |
| Custom Events | addEventListener + CustomEvent | ⚠️ |

### 17. Data Binding

| Blazor Pattern | JS-Mapping | Komplexität |
|---|---|---|
| `@bind="prop"` | value + onchange (2-way) | ⚠️ |
| `@bind:event="oninput"` | value + oninput | ⚠️ |
| `@bind:format` | Format-Funktion wrapper | 🔥 |
| `@bind:get` / `@bind:set` (.NET 7+) | Getter/Setter-Pair | ⚠️ |

### 18. Forms (v0 NICHT)

| Blazor Pattern | Status |
|---|---|
| `EditForm` | v0 NEIN ❌ |
| `DataAnnotationsValidator` | v0 NEIN ❌ |
| `ValidationSummary` | v0 NEIN ❌ |
| `InputText`, `InputNumber`, etc. | v0 NEIN ❌ |

**Begründung:** Komplex, Auth-nah, selten in Admin-Panels kritisch. Manuelle Forms mit `@bind` reichen für v0.

### 19. JS Interop

| Blazor Pattern | JS-Mapping | Komplexität |
|---|---|---|
| `IJSRuntime.InvokeAsync<T>("fn", args)` | direkter JS-Call | ✅ |
| `IJSRuntime.InvokeVoidAsync` | dito | ✅ |
| `[JSInvokable]` Methods | in Runtime registrieren | ⚠️ |
| `DotNetObjectReference` | direkter Objekt-Ref (trivial in JS) | ✅ |
| JS Module Import (`InvokeAsync("import", path)`) | dynamic import | ✅ |

### 20. CSS

| Blazor Feature | JS-Mapping | Komplexität |
|---|---|---|
| Scoped CSS (`Component.razor.css`) | übernehmen (Razor-Compiler rewritten schon Selectors) | ✅ |
| Deep Selector `::deep` | in rewritten Output schon enthalten | ✅ |
| Global CSS (`app.css`) | einfach einbinden | ✅ |

---

## Transpiler-Pipeline (konkret)

```
Input:  Projekt-Ordner (.csproj + .razor + .cs + .razor.css)
   │
   ▼
[1] Roslyn Workspace laden
   │   MSBuildWorkspace oder CSharpCompilation
   │
   ▼
[2] Razor-Compiler laufen lassen
   │   Microsoft.AspNetCore.Razor.Language
   │   → generiert .razor.g.cs Files
   │
   ▼
[3] Analyzer-Pass (Validation)
   │   ├─ @page Routes sammeln
   │   ├─ @inject Allowlist-Check
   │   ├─ BCL-Usage-Check gegen Subset
   │   ├─ Forbidden-Attributes-Check
   │   ├─ Forbidden-Features-Check (unsafe, dynamic, reflection)
   │   └─ → Violation-Report
   │   FAIL wenn Violations
   │
   ▼
[4] Transpiler-Pass
   │   Two Sub-Pipelines:
   │   
   │   (a) User-Code (C# Body):
   │       SyntaxTree → JS AST → JS Source
   │       via Roslyn SyntaxWalker + Emitter
   │   
   │   (b) RenderTreeBuilder-Code:
   │       Spezialisierter Walker erkennt __builder.* Calls
   │       → emittiert hyperscript h(...) Calls
   │
   ▼
[5] Runtime-Injection
   │   Import-Statements für @razorshave/runtime
   │   Bundled Runtime-Module einbinden
   │
   ▼
[6] Bundler (esbuild / rollup delegiert)
   │   ├─ Tree-Shaking
   │   ├─ Minification
   │   └─ Source Maps
   │
   ▼
[7] HTML-Generator
   │   ├─ index.html mit Entry-Script
   │   ├─ Route-Manifest als JSON
   │   └─ Asset-Copy (wwwroot, CSS)
   │
   ▼
Output: dist/
```

---

## Priorität für v0.1

**Must-have** (minimales Blazor-Template läuft):
- Alle ✅ Items in 1-10
- Grundlegende RenderTreeBuilder-Mappings (11)
- PageTitle, NavLink, Router, RouteView, CascadingValue (12)
- Alle Lifecycle-Hooks (13)
- [Client]-DI (14)
- HttpClient-Basics (15)
- Event Handling (16)
- @bind Basics (17)
- Scoped CSS (20)

**Later** (v0.2+):
- ⚠️ Items die nicht Must-have sind
- Forms (18)
- Advanced Pattern Matching
- Expression-Trees
- Struct value-semantics mit strikter Garantie

**Never** (explizit out-of-scope):
- Alle ❌ Items
- Auth-Components
- Server-only APIs
- Reflection

---

## Design-Entscheidungen

Feststehende Entscheidungen nach Durchgang der offenen Fragen.

### Projekt-Struktur & Validation

Siehe Sub-Doc: `RAZORSHAVE-PROJECT-STRUCTURE.md`

Kurz: Zwei-Projekt-Struktur (MyApp.Server + MyApp.Client), keine Conditional-Compilation, kein Two-World-Problem. ApiClient-Pattern für allen Daten-Access. Validation via strikte Allowlist (nicht Blacklist). User kann Packages opt-in erlauben über `razorshave.json`.

### Runtime-API-Kontrakt

Siehe Sub-Doc: `RAZORSHAVE-RUNTIME-API.md`

Kurz: Component-Base mit Blazor-kompatibler Lifecycle-API, keyed VDOM-Diff (Preact-Level), eigener Router mit vollem Blazor-Syntax-Support, Singleton-only DI, generische Store-Runtime, ApiClient mit voller HTTP-Features, IJSRuntime für JS-Interop, alle EventArgs-Typen als PascalCase-Wrapper. Bundler ist esbuild embedded. Target Bundle-Size: ~150-250 KB gzipped total für mittelgroße Apps.

### Roslyn-Integration & Build-Flow

Siehe Sub-Doc: `RAZORSHAVE-ROSLYN-BUILD.md`

Kurz: MSBuild-Target-Integration via `.targets`-File, läuft nach normalem `dotnet build`. Source-basiert (nicht IL). **Nur Source-Code wird transpiliert, niemals DLLs** — externe NuGet-Packages ohne Source sind nicht transpilierbar. Razorshave sammelt User-Sources + generated Sources + ProjectReference-Sources, baut Roslyn-Compilation, validiert gegen Allowlist, transpiliert zu JS, bundled mit esbuild zu `dist/`. Gesamt-Build-Zeit: ~10-30 Sekunden.

### Test-Strategie

Siehe Sub-Doc: `RAZORSHAVE-TESTING.md`

Kurz: differenzierte Coverage-Ziele pro Layer. Transpiler/Analyzer/Source-Generator: 95%+ TDD mit Snapshot-Tests (Verify). Runtime pure Logic: 90%+ TDD mit Vitest. Runtime-DOM: 70% mit Playwright. MSBuild-Task: manueller Smoke-Test-Katalog (10-15 Szenarien). End-to-End: eine Kitchen-Sink-App die alle Features zeigt und gleichzeitig Test-Target, Live-Demo und Bundle-Size-Benchmark ist. CI-Gates blockieren PRs bei Coverage-Unterschreitung.

### M0 — Proof-of-Concept

Siehe Sub-Doc: `RAZORSHAVE-M0.md`

Kurz: erster konkreter Milestone. Das minimal angepasste "dotnet new blazor" (+ 1 Zeile AddRazorshave + Weather zu ApiClient umgestellt) läuft transpiliert im Browser. Definition-of-Done: 15 manuelle Checks. Beweist Transpiler, Runtime, Router, ApiClient, VDOM-Diff, MSBuild-Integration. Danach: informierte Do-or-Don't-Entscheidung. Aufwand: 6-10 Wochen Vollzeit bzw. 6-12 Monate Wandzeit bei Abend-Arbeit.

### Bootstrap-Plan

Siehe Sub-Doc: `RAZORSHAVE-BOOTSTRAP.md`

Kurz: Wie anfangen. Sechs Haupt-Schritte (Repo, Skeleton, CI, Kitchen-Sink, Implementation, M0-Entscheidung). Schritt 5 detailliert in 14 Sub-Schritten von Roslyn-Exploration über ersten Transpiler-Walker bis kompletter Kitchen-Sink-Durchlauf. Runtime und Transpiler können parallel entwickelt werden. Gesamt-Wandzeit bei Abend-Arbeit: 9-13 Monate.

### Code Quality & Guidelines

Siehe Sub-Doc: `RAZORSHAVE-CODE-QUALITY.md`

Kurz: Drei Kern-Regeln die über Sauberkeit entscheiden — saubere Abstraktion (alles mockbar), keine Spaghetti, Single Responsibility. Standard-.NET-Conventions für C#, Pure JavaScript ohne TypeScript für Runtime, freier Commit-Stil, XML-Docs nur auf Public API, Fail-Fast Error-Handling, minimale Dependencies (kein React/Vue/Lodash), Korrektheit > Lesbarkeit > Performance. Review-Prozess ist interaktiv: nach jedem Consors-Durchlauf reviewed Bernhard.

### Elevator Pitch

Siehe Sub-Doc: `RAZORSHAVE-PITCH.md`

Kurz: Kompakte Erklärungs-Varianten für Kollegen, Fachgespräche und Dev-Community. Plus Differenzierungs-Tabelle gegen Blazor WASM, Blazor Server und React/Vue.

### Build-Flow

Siehe Sub-Doc: `RAZORSHAVE-BUILD-FLOW.md`

Kurz: Razorshave ist Post-Build-Transpiler, hookt sich als MSBuild-Target ein. User nutzt `dotnet build -c Razorshave`. MSBuild macht Compile + Source-Generators + Razor-Generation, Razorshave sammelt dann den generierten Source, transpiliert zu JS, bundelt mit esbuild, schreibt `dist/`.

### State Management

**`IStore<T>` als typisiertes Singleton-Interface.** Ein Konzept für alle State-Szenarien.

```csharp
public interface IStore<T>
{
    // Core
    T? Get(string key);
    void Set(string key, T value);
    void Delete(string key);
    IReadOnlyList<T> GetAll();
    
    // Convenience (alle v0)
    void Clear();
    int Count { get; }
    bool Has(string key);
    IEnumerable<T> Where(Func<T, bool> predicate);
    
    // Batching
    void Batch(Action updates);
    
    // Notifications
    event Action OnChange;
}
```

**Usage:**
```razor
@inject IStore<CartItem> Cart

@code {
    protected override void OnInitialized() => Cart.OnChange += StateHasChanged;
    public void Dispose() => Cart.OnChange -= StateHasChanged;
}
```

**Eigenschaften:**
- String-Keys (einfach, User kann ToString() auf Int/Guid-IDs nutzen)
- Mehrere Stores pro App erlaubt, jeder typisiert
- Funktioniert 1:1 in Blazor Server Dev (als Default-Impl mit in-memory Dictionary)
- Transpiler mappt zu einer Runtime-JS-Class mit Map-Backing + Event-Emitter
- Keine Property-Level-Reactivity: `OnChange` feuert bei jeder Mutation, Component entscheidet selbst ob rerender

**Nicht dabei:** Proxy-basierte Auto-Reactivity, Signal-System, INotifyPropertyChanged-Emit, computed Properties als Runtime-Feature. Keep it simple.

### Source Maps

**Nicht in v0.** User sieht im Browser-DevTools den JS-Code. Debug in C# → Blazor Server Dev-Mode in Visual Studio/Rider. Debug im Browser → JS. Klare mentale Trennung: "Razorshave ist ein Compiler, kein Emulator."

Opt-in-Feature für v0.2+ wenn User-Bedarf entsteht.

### Build-Mode

**Kein Hot Reload, kein Watch-Mode im Build.** `razorshave build` = einmal bauen, fertig. Hot Reload gibt's im Dev via Blazor Server's nativem .NET Hot Reload.

### TypeScript-Definitions-Export

**Nicht in v0.** Zielgruppe schreibt kein externes JS gegen Razorshave-Apps.

### Paket-Struktur (NuGet)

Drei Pakete:

- **`Razorshave.Cli`** — `dotnet tool` Global Tool, enthält Transpiler + Build-Logic
- **`Razorshave.Analyzer`** — NuGet im User-Projekt, IDE-Integration via Roslyn-Analyzer, Violation-Squiggles in VS/Rider während des Schreibens (vor dem Build)
- **`Razorshave.Abstractions`** — NuGet im User-Projekt, enthält `[Client]`, `IStore<T>`, `ApiClient` Basisklasse — alle Typen die User-Code referenziert

Runtime (JavaScript) wird vom CLI embedded, kein separates Paket.

### decimal-Support

**`decimal.js-light` eingebettet** (12.7 KB minified, MIT License).

Transpiler mappt:
- `decimal x = 19.99m` → `const x = new Decimal("19.99")`
- `a + b` (decimal-Operanden) → `a.plus(b)` (via Operator-Overload-Rewrite)
- `(double)d` → `d.toNumber()`
- `d.ToString()` → `d.toString()`

Tree-Shakeable: wer kein decimal nutzt, bekommt die Lib nicht im Bundle.

### long-Präzision

**Kein spezielles Handling.** `long` wird zu JS `number`. User die DB-IDs vom Frontend lesen: **tun sie nicht, ist API-Sache.** Wer Präzisionsprobleme bekommt, kriegt einen Compile-Error mit Hinweis.

BigInt-Support: v0.2 wenn jemand wirklich danach fragt.

### Reactivity-Modell

**Manuelles `StateHasChanged()` + Auto-Trigger nach Event-Handlern.** Exakt wie Blazor. Kein Proxy-Magic, kein Signal-System.

- Runtime wrapped Event-Handler: nach Ausführung → `StateHasChanged()` auf dem Component
- User kann manuell `StateHasChanged()` callen (nach async-Operations, etc.)
- Store-subscriptions triggern explizit via `store.OnChange += StateHasChanged`

### Structs & Records

**Struct = Class behandelt.** Keine Value-Semantik, Reference-Semantik wie Classes. Gleich für Records (in v0.1 keine Value-Equality-Generation, kein with-Expression-Support).

User der Value-Semantik will: manuelles `.Clone()`.

Begründung: Strukturen mit echter Value-Semantik sind im Frontend-Code praktisch nicht vorhanden. v0 spart Implementierung, v0.2 kann Record-Equality nachziehen wenn Bedarf besteht.

### Multiple Constructors

**Nicht supported.** Eine Klasse = ein Constructor. Sonst Compile-Error.

User-Workarounds: Default-Parameters, Factory-Methoden, Named-Arguments.

### Indexer (`this[]`)

**Nicht in v0.** User nutzt explizite Get/Set-Methoden. Wenn wichtig: v0.2.

### Operator Overloading

**Via Method-Rewrite.** Transpiler erkennt user-definierte Operatoren via Roslyn SemanticModel und rewritet Expressions:

- `price + tax` (wenn beide `Money`) → `Money.op_Addition(price, tax)`
- Binary-Operators (+, -, *, /, %, ==, !=, <, >, <=, >=, &, |, ^): v0
- Unary-Operators (-x, !x, ++x, --x): v0
- Conversion-Operators (`implicit operator decimal`): v0.2

### Forms

**Nicht in v0.** `EditForm`, `DataAnnotationsValidator`, `InputText`/`InputNumber`/etc. → Compile-Error mit Hinweis. User baut Forms manuell mit `@bind` (funktioniert).

v0.2 kann EditForm nachziehen.

### Auth

**Nicht in v0.** Razorshave-Apps machen Auth über die API (JWT/OAuth tokens via HttpClient-Header-Injection). Blazor Identity / Cookie-Auth → Compile-Error.

---

### UI-Library-Strategie

**Keine Blazor-UI-Libraries.** Telerik, DevExtreme, Syncfusion, MudBlazor, Radzen, Ant Design Blazor, FluentUI Blazor → alle nicht unterstützt (nutzen Blazor-spezifische Infrastruktur, JS-Interop-Patterns, oft proprietäre Services).

**Stattdessen: volle npm-Welt.** Razorshave-Apps kompilieren zu JavaScript — User können JS/TS-Libraries direkt einbinden:

- **Charts:** Chart.js, ApexCharts, Recharts, D3, Plotly
- **Tables:** AG Grid, TanStack Table, DataTables
- **Forms/Inputs:** React Hook Form, Zod, Yup (über JS-Interop)
- **Editors:** Monaco, CodeMirror, Tiptap, Quill
- **Maps:** Leaflet, Mapbox GL JS
- **UI-Kits:** Shoelace (Web Components, framework-agnostic), Tailwind, Bootstrap
- **Animation:** Framer Motion (wenn kompatibel), GSAP, Anime.js
- **3D:** Three.js, Babylon.js

Integration via `IJSRuntime` (gewohnt aus Blazor) oder dediziertes `[JsModule]` Attribute (Design offen, v0.2).

**Positioning:**

> "Blazor-Syntax mit Zugriff auf das gesamte npm-Ökosystem."

Wer Telerik-Components braucht → nutzt klassisches Blazor. Wer AG Grid braucht → nutzt Razorshave.

---

## Offene Fragen (noch zu entscheiden)

_Keine aktuell. Alle initialen Design-Fragen beantwortet._

---

## Naming

Name: **Razorshave** (tentativ, frei auf nuget/npm/github)

Metapher: Blazor wird auf das Client-only-Minimum "rasiert". Merkbar, erklärbar, positioniert klar gegenüber Blazor.

Alternativen:
- Scalpel (professioneller Vibe)
- Filet (witzig, aber Food-Assoziation)
- Cinder, Flint, Shaver, Splinter

Domain-Check offen: razorshave.dev / razorshave.com / razorshave.io

---

## Nächste Schritte

1. Razor-Compiler-Output einer Counter.razor analysieren (verstehen was Razorshave's Transpiler eigentlich eats)
2. Proof-of-Concept M0: manueller Transpiler + Runtime für Counter
3. Namens-Domain-Check
4. Navio-Input anlegen oder als Backlog-Ordner archivieren

---

## Notes

- Dev läuft auf unverändert `dotnet new blazor` (minimales Template, kein Auth, kein EF)
- Kein Ziel: "100% Blazor-Kompatibilität" — explizit ein eigenes Framework mit eigenen Regeln
- Inspiration: Fable (F#→JS), Svelte (Compile-Time-Framework, keine Runtime-Magic)
- Business-Modell: offen — OSS mit Pro-Features? Dual-License? Reine OSS?
