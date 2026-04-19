# Razorshave: Roslyn-Findings

> Was Roslyn aus Razor-generiertem Code wirklich zurückgibt. Referenz für die Transpiler-Walker (5.3+).

**Status:** Schritt 5.1 durchlaufen — Counter, Home, Weather, MainLayout, NavMenu, App, Routes inspiziert.
**Parent:** RAZORSHAVE-BOOTSTRAP.md
**Datum:** 2026-04-19

---

## Reproduction

```bash
# .g.cs generieren
dotnet build e2e/KitchenSink.Client/KitchenSink.Client.csproj

# Explorer auf Counter (default)
dotnet run tools/RoslynExplorer.cs

# Explorer auf beliebiges Target
dotnet run tools/RoslynExplorer.cs -- e2e/KitchenSink.Client/obj/.../Weather_razor.g.cs
```

Generated-Files liegen in:
```
e2e/KitchenSink.Client/obj/Debug/net10.0/generated/
  Microsoft.CodeAnalysis.Razor.Compiler/
    Microsoft.NET.Sdk.Razor.SourceGenerators.RazorSourceGenerator/
      Components/
        Pages/{Counter,Home,Weather,…}_razor.g.cs
        Layout/{MainLayout,NavMenu,…}_razor.g.cs
        {App,Routes,_Imports}_razor.g.cs
```

`KitchenSink.Client.csproj` hat `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` damit diese persistent bleiben. Name-Konvention: `Counter.razor` → `Counter_razor.g.cs` (Punkt zu Unterstrich).

---

## Frage 1: Wie kommen wir an die `.razor.g.cs`?

Aus `obj/Debug/<tfm>/generated/…/Components/…/X_razor.g.cs`. Razor-Source-Generator schreibt sie bei Build dahin, sofern `EmitCompilerGeneratedFiles=true` (sonst nur in-memory). Razorshave muss den Razor-Compiler **nicht direkt** aufrufen — MSBuild macht das bereits und wir lesen nur das Resultat.

**Konsequenz:** In 5.13 (MSBuild-Integration) sammelt Razorshave diese Files aus `$(CompilerGeneratedFilesOutputPath)`. Für 5.3-5.8 committen wir Fixture-Files direkt.

---

## Frage 2: Wie sieht der rohe Output aus?

Jede Component → eine `public partial class X : ComponentBase` (bzw. `: LayoutComponentBase` bei Layouts).

- Namespace = Ordner-Pfad (`KitchenSink.Client.Components.Pages`)
- `@using` aus `_Imports.razor` werden eingebettet (jedes in eigenem `#nullable restore/disable`-Block)
- Class-Attribute aus `@page`, `@attribute`, `@rendermode`
- **Alle** Razor-Markup- und Embedded-C#-Teile landen in **einer** Method `BuildRenderTree(RenderTreeBuilder __builder)`
- `@code`-Block (Fields, Properties, Methods, Nested-Classes) steht **als Member der Klasse** — nicht in BuildRenderTree
- `#line`-Pragmas verweisen auf die `.razor`-Quelle → wichtig für Diagnostics-Locations (5.14)

**Konsequenz:** Unser Transpiler kriegt saubere C#-Klassen. Die Razor-Spezifik ist reduziert auf: **ein** `BuildRenderTree` mit `__builder`-Calls plus normale C#-Member.

---

## Frage 3: SyntaxTree-Struktur

~250-650 Descendant-Nodes pro Component. Root ist `CompilationUnit` → `NamespaceDeclaration` → `ClassDeclaration`.

Relevante Node-Typen:
- `ClassDeclarationSyntax.Modifiers` (`public partial`)
- `ClassDeclarationSyntax.BaseList.Types` (einziger Base-Type, bei uns `ComponentBase` oder `LayoutComponentBase`)
- `ClassDeclarationSyntax.AttributeLists` (`[RouteAttribute]`, `[StreamRendering]`, `[__PrivateComponentRenderModeAttribute]`, …)
- `ClassDeclarationSyntax.Members` → Methods / Fields / Properties / nested Classes

Render-Tree-Walker greift auf:
```csharp
buildMethod.DescendantNodes()
    .OfType<InvocationExpressionSyntax>()
    .Where(inv =>
        inv.Expression is MemberAccessExpressionSyntax mae
        && mae.Expression is IdentifierNameSyntax id
        && id.Identifier.Text.StartsWith("__builder"));  // __builder, __builder2, __builder3…
```

**Konsequenz:** Zwei unabhängige Walker-Stufen — User-Code-Walker auf `class.Members`, Render-Tree-Walker auf `BuildRenderTree.Body`. Keine File-Trennung nötig, da beide auf derselben partial class arbeiten.

---

## Frage 4: Klasse selbst

Beispiele:

| Component | Base | Class-Attribute |
|---|---|---|
| `Counter` | `ComponentBase` | `[RouteAttribute("/counter")]`, `[__PrivateComponentRenderModeAttribute]` |
| `Home` | `ComponentBase` | `[RouteAttribute("/")]` |
| `Weather` | `ComponentBase` | `[StreamRendering]`, `[RouteAttribute("/weather")]` |
| `MainLayout` | `LayoutComponentBase` | _(keine)_ |
| `NavMenu` | `ComponentBase` | _(keine)_ |
| `App` | `ComponentBase` | _(keine)_ |
| `Routes` | `ComponentBase` | _(keine)_ |

**Konsequenzen:**
- `[RouteAttribute]` ist der Hook für die Route-Table-Collection (5.12)
- `LayoutComponentBase` ist eine weitere Base-Class (neben `ComponentBase`) — Razorshave-Runtime braucht beide
- `[__PrivateComponentRenderModeAttribute]` ignorieren (entspricht der entschiedenen Regel „`@rendermode` wird ignoriert")
- `@attribute [StreamRendering]` → normale C#-Class-Attribute — ebenfalls ignorieren (SSR-spezifisch)

---

## Frage 5: `SemanticModel` auf den `__builder.*`-Calls ✅

Für eine brauchbare Semantic-Model-Resolution muss die `CSharpCompilation` gegen **Microsoft.NETCore.App + Microsoft.AspNetCore.App Shared Framework + KitchenSink.bin** referenziert werden. Der Explorer macht das automatisch (329 Assemblies).

**Was `SemanticModel.GetSymbolInfo()` auf den Invocations liefert:**

```
[0]  __builder.OpenComponent<PageTitle>    →   Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder.OpenComponent<PageTitle>
[1]  __builder.AddAttribute(…)             →   Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder.AddAttribute
[14] __builder.AddAttribute(…, EventCallback.Factory.Create<MouseEventArgs>(…))
                                           →   Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder.AddAttribute<MouseEventArgs>
```

Alle resolvable. Bei generischen Methoden gibt `IMethodSymbol.TypeArguments` den konkreten Typ zurück (`MouseEventArgs` etc.).

**Was `GetSymbolInfo` auf Argumenten liefert:**

| Argument | Symbol | Type |
|---|---|---|
| `currentCount` | `IFieldSymbol` (int) | `int` |
| `forecast.Date.ToShortDateString()` | `IMethodSymbol` (`DateOnly.ToShortDateString`) | `string` |
| `forecast.TemperatureC` | `IPropertySymbol` | `int` |
| `Body` (in MainLayout) | `IPropertySymbol` (aus LayoutComponentBase) | `RenderFragment?` |
| `Assets["app.css"]` (in App) | `IPropertySymbol` (Indexer `this[]`) | `string` |
| `(RenderFragment)((__builder2) => {…})` | null (complex) | `RenderFragment` |
| `EventCallback.Factory.Create<T>(this, …)` | null (complex on the whole call) | — |

**Konsequenz:**
- Für 99% aller Walker-Entscheidungen reicht der `IMethodSymbol` der Invocation (ContainingType + Name).
- Expression-Rewriting für User-Code (`currentCount`, `forecast.TemperatureC`) bekommt Field/Property-Symbols sauber zurück — Transpiler kann unterscheiden was Field vs Property vs Method ist → nötig für JS-Access-Emission (`this.fieldName` vs `this.methodName()`).
- Cast-Expressions, Lambdas, generische Factory-Calls landen als „complex" (kein Single-Symbol) — Walker muss tiefer gehen (in die `ArgumentList.Arguments` des generierten RenderFragment-Lambdas).
- Allowlist-Check aus RAZORSHAVE-PROJECT-STRUCTURE.md (`symbol.ContainingAssembly` testen) funktioniert — das zeigt unser Test für `DateOnly.ToShortDateString` → kommt aus `System.Private.CoreLib.dll`, also **NICHT** in unserer `SupportedBclNamespaces`-Liste. Das **wird zum Compile-Error** im Analyzer/Transpiler — User muss z.B. `date.ToString("d")` nutzen. Offener Design-Punkt.

---

## Frage 6: Event-Handler-Emission ✅

Raw-Output (Counter `<button @onclick="IncrementCount">`):
```csharp
__builder.AddAttribute(12, "onclick",
  global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create<
    global::Microsoft.AspNetCore.Components.Web.MouseEventArgs>(
      this, IncrementCount));
```

**Zwei Erkennungs-Muster — beide sauber:**

1. **Textuell (syntaktisch):** Argument 2 ist eine `InvocationExpression` deren Member-Access auf `EventCallback.Factory.Create` endet. Funktioniert ohne SemanticModel.

2. **Semantisch (via SemanticModel):** Der `IMethodSymbol` der äußeren `AddAttribute`-Call ist die **generische Überladung** `AddAttribute<T>` — `symbol.IsGenericMethod && symbol.TypeArguments[0]` gibt uns den EventArgs-Typ (`MouseEventArgs` etc.) direkt.

Methode 2 ist **kleiner und robuster** — wir müssen nicht das innere `EventCallback.Factory.Create` parsen, Roslyn hat den EventArgs-Typ schon aufgelöst.

**Walker-Plan (5.7):**
- Detect: `inv.Symbol is IMethodSymbol m && m.IsGenericMethod && m.Name == "AddAttribute"`
- `eventArgsType = m.TypeArguments[0]`
- Parse arg 2 (das EventCallback): suchen die **zweite Argument-Expression** des inneren `Create<T>(this, X)`-Calls → das ist der Handler (Method-Ref oder Lambda)
- Emittiere: `props["on" + name] = (e) => <transpiled handler>(new <EventArgsType>(e))`

---

## Frage 7: `@page`-Attribute ✅

Landen als **ganz normales C#-Class-Attribute** `[RouteAttribute("/counter")]`.

**Konsequenz für Router (5.12):**
- Walker sammelt alle `ClassDeclarationSyntax` deren Attribute `RouteAttribute` matchen (fully-qualified: `Microsoft.AspNetCore.Components.RouteAttribute`, via SemanticModel resolved)
- Erstes Argument ist der Route-Pattern-String — konstant zur Build-Zeit
- Output: `{ pattern: "/counter", component: Counter }` ins Route-Manifest (JS-Objekt)
- Multiple `@page` auf einer Component → mehrere `[RouteAttribute]`-Instanzen nebeneinander — wir iterieren AttributeLists normal

---

## Frage 8: Sequence-Numbers ✅

Jeder `__builder.*`-Call hat als erstes Argument eine Sequence-Nummer. Blazor Server nutzt sie für Diff-Optimierung. **Razorshave-Diff ist keyed und braucht sie nicht** — der Walker ignoriert `args[0]`.

`CloseElement()` / `CloseComponent()` haben keine Sequence-Nummer — weil sie nur Open-Close-Balance signalisieren.

---

## Neue Erkenntnisse (in den Plan-Docs nicht erwähnt)

### `@if` / `@foreach` werden als plain-C# emittiert ✅ (wichtig)

Razor übersetzt Markup-Control-Flow **in normales C# Statement-Nesting** innerhalb von `BuildRenderTree`:

```csharp
if (forecasts == null)
{
    __builder.AddMarkupContent(6, "<p><em>Loading...</em></p>");
}
else
{
    __builder.OpenElement(7, "table");
    // …
    foreach (var forecast in forecasts)
    {
        __builder.OpenElement(11, "tr");
        __builder.OpenElement(12, "td");
        __builder.AddContent(13, forecast.Date.ToShortDateString());
        __builder.CloseElement();
        // …
    }
    __builder.CloseElement();
}
```

**Konsequenz (massiv für Walker-Design):**
- Walker muss keine spezielle `@if` / `@foreach`-Logik haben
- C#-Control-Flow wird vom **normalen** User-Code-Walker transpiliert (5.4-5.5)
- Render-Tree-Walker ist eine **Expression-Rewrite-Stufe** innerhalb dieses C# — jede `__builder.*`-Invocation wird zum `h(...)`-Call

**Aber:** Blazor's `__builder.OpenElement(…) / __builder.CloseElement()` pattern ist **stateful über Statement-Grenzen hinweg**. Unser JS-Äquivalent ist ein nested `h('tag', props, children)` — funktional, nicht imperativ. Für M0 bauen wir das Walker-intern als **virtuellen Stack**:
- Open-Call pusht einen Frame `{ tag, props: [], children: [] }`
- AddAttribute appendiert an `frame.props`
- AddContent/AddMarkupContent appendiert an `frame.children`
- Close-Call popt Frame, emittiert `h(tag, props, children)` als Expression, appendiert in Parent's children
- Bei Control-Flow (if/foreach): Walker muss Kinder-Sammeln via `children.push(...)` statt einer einfachen VDOM-Collection — weil die Zahl der Kinder dynamisch ist

Die JS-Emission wird dadurch gemischt-imperativ:
```js
render() {
  const children = [];
  children.push(h('h1', {}, 'Weather'));
  if (this.forecasts == null) {
    children.push(h('p', {}, h('em', {}, 'Loading...')));
  } else {
    const rows = [];
    for (const forecast of this.forecasts) {
      rows.push(h('tr', {}, [
        h('td', {}, forecast.Date.toShortDateString()),
        // …
      ]));
    }
    children.push(h('table', { class: 'table' }, [
      h('thead', {}, /* markup */),
      h('tbody', {}, rows),
    ]));
  }
  return h('div', {}, children);
}
```

Für M0 akzeptabel. v0.2 kann das in `.map()`-Pattern rewriten.

### `AddComponentParameter` — zweite Method neben `AddAttribute` ✅

In NavMenu und Routes nutzt Razor `__builder.AddComponentParameter(seq, name, value)` für Component-Props. In Counter nutzt er `AddAttribute(…, "ChildContent", RenderFragment)`.

**Heuristik von Razor** (beobachtet):
- `AddComponentParameter` für **simple-typed** Parameter (string, int, Type, bool)
- `AddAttribute` für **RenderFragment** und **EventCallback** (die immer generisch-resolvierbar sind)

**Konsequenz für Walker:** Beide Methoden setzen einen Prop — `props[name] = value`. Walker behandelt sie identisch.

### `LayoutComponentBase.Body` — Body-Property für Layouts ✅

MainLayout:
```csharp
public partial class MainLayout : LayoutComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder __builder)
    {
        __builder.OpenElement(0, "div");
        // …
        __builder.AddContent(1, Body);  // <- property from LayoutComponentBase
        // …
    }
}
```

`Body` ist `RenderFragment?` auf `LayoutComponentBase`. Unsere Runtime braucht:
- `LayoutComponentBase` als Base-Class (neben `Component`)
- Property `body` wird vom Router/RouteView befüllt wenn eine Page in diesem Layout rendert

### `Routes.razor` wrapped Blazor's `Router` ⚠ Special-Case

Routes.razor transpiliert zu:
```csharp
__builder.OpenComponent<Router>(0);
  __builder.AddComponentParameter(1, "AppAssembly", typeof(…).Assembly);
  __builder.AddComponentParameter(2, "NotFoundPage", typeof(NotFound));
  __builder.AddAttribute(3, "Found", (RenderFragment<RouteData>)((routeData) => (__builder2) => {
    __builder2.OpenComponent<RouteView>(4);
      __builder2.AddComponentParameter(5, "RouteData", routeData);
      __builder2.AddComponentParameter(6, "DefaultLayout", typeof(MainLayout));
    __builder2.CloseComponent();
    __builder2.AddMarkupContent(8, "\r\n        ");
    __builder2.OpenComponent<FocusOnNavigate>(9);
      __builder2.AddComponentParameter(10, "RouteData", routeData);
      __builder2.AddComponentParameter(11, "Selector", "h1");
    __builder2.CloseComponent();
  }));
__builder.CloseComponent();
```

**Konsequenz:** Razorshave-Transpiler **ersetzt** die Routes.razor-Transpilation durch Build-Time-Extraktion:
- Scan für alle Components mit `[RouteAttribute]` → Route-Manifest
- Aus Routes.razor extrahieren wir: `DefaultLayout` (typeof-Argument) und `NotFoundPage`
- `Router`, `RouteView`, `FocusOnNavigate` werden **nicht** als Components transpiliert — sie sind Blazor-spezifisch
- Die Razorshave-Runtime hat eigenen Router der Route-Manifest + Layout + NotFound selbst handled

### `App.razor` generiert HTML-Shell ⚠ Special-Case

App.razor enthält das volle `<!DOCTYPE html><html><head>…</head><body>…</body></html>`-Skelett plus:
- `<ImportMap />` — Blazor-spezifisch
- `<HeadOutlet />` — Blazor-spezifisch
- `<Routes />` — unser Routen-Einstieg
- `<ReconnectModal />` — Blazor-Server SignalR-Reconnect

Plus `Assets["path"]`-Indexer für bundled Asset-URLs.

**Konsequenz:** Razorshave-Transpiler **generiert eigenes `index.html`** — ignoriert App.razor oder extrahiert nur den HTML-Skelett-Teil. Blazor-spezifische Components werden nicht transpiliert. `Assets["path"]` → Build-Time-Resolution über esbuild's Content-Hashes.

### `RenderFragment<T>` ✅ (typed RenderFragment)

In Routes.razor: `(RenderFragment<RouteData>)((routeData) => (__builder2) => {…})`. Doppelte Lambda:
- Äußere nimmt `T` (hier `RouteData`)
- Gibt innere Lambda zurück die `__builder2` nimmt und darauf rendert

**Runtime-Äquivalent:** `(data) => (children) => h(…)` bzw. als VDOM-Function-Component: nimmt `data`, returnt VDOM.

**Konsequenz:** Runtime's `RenderFragment` ist eine Funktion `(children, ...args) => vnode[]`. Generic-Variante `RenderFragment<T>` = `(t) => (children) => vnode[]`.

### `@attribute [X]` ✅ (plain Class-Attribute)

`@attribute [StreamRendering]` landet als ganz normales `[StreamRendering]`-Class-Attribute im .g.cs. Unser Analyzer/Transpiler sieht nichts außergewöhnliches — entweder Allowlist (passiert durch), oder es ist ein Server-Only-Attribute wie StreamRendering → Compile-Error / Ignore.

Für StreamRendering konkret: ist Blazor-Server-Spezifisch — unser Analyzer sollte es **warnen aber nicht errorn** (es hat keine Runtime-Konsequenz in der transpilierten App, es wird einfach ignoriert).

---

## Mapping-Tabelle: beobachtete `__builder`-Methoden

| Method | Signatur (vereinfacht) | VDOM-Semantik |
|---|---|---|
| `OpenElement(seq, name)` | `(int, string)` | Push-Element-Frame |
| `CloseElement()` | `()` | Pop-Frame → emit `h(tag, props, children)` |
| `OpenComponent<T>(seq)` | `(int)` | Push-Component-Frame, resolved type = T |
| `CloseComponent()` | `()` | Pop-Frame → emit `h(ComponentClass, props, children)` |
| `AddAttribute(seq, name, value)` | `(int, string, object)` | `frame.props[name] = value` (element or component) |
| `AddAttribute<T>(seq, name, EventCallback<T>)` | generic overload | Event-Handler-Wrap (siehe Frage 6) |
| `AddComponentParameter(seq, name, value)` | `(int, string, object)` | `frame.props[name] = value` (component only, simple-typed) |
| `AddContent(seq, text)` | `(int, string)` | `frame.children.push(textNode(text))` |
| `AddContent(seq, expr)` | `(int, object)` | `frame.children.push(stringify(expr))` |
| `AddContent(seq, RenderFragment)` | `(int, RenderFragment)` | Expand RenderFragment-Lambda inline |
| `AddMarkupContent(seq, rawHtml)` | `(int, string)` | `frame.children.push(rawHtmlNode(rawHtml))` — braucht Runtime-Support |

**Nicht in unserer Fixture gesehen, sollten später geprüft werden:** `SetKey`, `AddElementReferenceCapture`, `AddComponentReferenceCapture`, `OpenRegion`/`CloseRegion`.

---

## Offene Untersuchungen (für spätere Fixtures)

- [ ] `@bind="x"` — zwei AddAttribute-Calls (value + onchange)? Oder eins mit Bind-spezifischem Helper?
- [ ] `@inject` — wie wird das am Class-Member emittiert? Property mit `[Inject]`-Attribute?
- [ ] `@code { private Xyz Svc { get; set; } = default!; }` mit `[Inject]` — sieht Razor-Compiler das anders?
- [ ] `@key="x"` — wird SetKey aufgerufen?
- [ ] `@ref="x"` — AddElementReferenceCapture / AddComponentReferenceCapture
- [ ] Mehrere `@page` auf einer Component (multi-routes)
- [ ] `@typeparam` — generische Components (wie wird Generic-Class emittiert?)
- [ ] Nested RenderFragments mit Parameter-Signaturen
- [ ] Assets-Indexer genauer: woher kommt die Property `Assets`? Welches Interface/Class?

Diese werden beim Ausbau der Kitchen-Sink und spätestens in 5.11 / 5.12 beantwortet.

---

## Design-Entscheidungen die aus 5.1 gefestigt sind

1. **Zwei Walker-Stufen:**
   - `UserCodeEmitter` (Methods, Fields, Properties, Expressions, Control-Flow) — 5.3-5.5
   - `RenderTreeRewriter` (rewritet `__builder.*`-Invocations innerhalb von BuildRenderTree zu `h()`-Calls, behält umgebendes C# bei) — 5.6-5.7

2. **Builder-Stack im RenderTree-Walker:** Open/Close-Balance via virtuellem Stack, Emission erfolgt beim Close. Nested RenderFragments (`__builder2`, `__builder3`) erfordern rekursive Instanziierung des Walkers mit eigenem Stack.

3. **SemanticModel ist erforderlich, nicht optional.** Für Symbol-Resolution (Allowlist-Check, User-Field-vs-Method-Unterscheidung, generische Type-Args bei EventCallback) brauchen wir die volle Compilation. Razorshave-Build muss sicherstellen dass alle Assemblies (Shared Framework + User-bin) als References vorliegen.

4. **Special-Cases per Class-Name / Type-Identity:**
   - `Routes` → nicht transpilieren, stattdessen Route-Manifest extrahieren
   - `App` → eigener `index.html` generieren, App-Markup teilweise oder gar nicht übernehmen
   - `Router`, `RouteView`, `FocusOnNavigate` → Runtime-Built-ins, nicht als Components transpiliert
   - `HeadOutlet`, `ImportMap`, `ReconnectModal` → ignoriert / weggeschnitten

5. **Zwei Component-Base-Classes in Runtime:** `Component` und `LayoutComponentBase` (letzteres mit `Body`-Property für Layout-Slots).

6. **RenderFragment als Funktion** `(children, ...args) => vnode[]`, Generic-Variante `(t) => (children) => vnode[]`.

7. **Zwei Prop-Setter-Methoden** (`AddAttribute` und `AddComponentParameter`) mappen auf dasselbe `props[name] = value` — Walker behandelt sie austauschbar.

8. **Event-Handler-Detection via `IMethodSymbol.IsGenericMethod` auf `AddAttribute<T>`** — nicht über textuelles Matching auf `EventCallback.Factory.Create`.

9. **Sequence-Numbers werden ignoriert** (wie ursprünglich geplant).

10. **`@if`/`@foreach` brauchen kein Sondercode** — werden als plain C#-Control-Flow transpiliert, nur die inneren `__builder.*`-Calls werden rewritten.
