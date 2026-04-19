# Razorshave: Projekt-Struktur & Validation

> Wie User ihre Razorshave-Projekte aufbauen, wie Client mit Server redet, und wie Razorshave validiert dass nur zulässiger Code transpiliert wird.

**Status:** Design-Draft
**Parent:** RAZORSHAVE.md
**Datum:** 2026-04-19

---

## Kern-Prinzip: Kein Two-World-Problem

**Razorshave-Code ist Razorshave-Code.** Dev und Build verhalten sich identisch in der Daten-Schicht. Es gibt keine Conditional-Compilation, keine zweiten Implementations, keine Magic-Switches zwischen Welten.

Der einzige Unterschied zwischen Dev und Build:
- **Dev:** Render-Loop läuft über SignalR (Blazor Server)
- **Build:** Render-Loop läuft clientseitig (Razorshave-Runtime-VDOM)

Alles andere — Daten-Access, Business-Logik, State, API-Calls — ist in beiden Welten **identisch**.

---

## Projekt-Struktur

Standard-Layout:

```
MyApp/
├── MyApp.Server/           ← klassisches ASP.NET Core
│   ├── Controllers/        ← REST-API-Endpoints
│   ├── Data/               ← EF Core, DB-Zugriff
│   └── Program.cs
│
├── MyApp.Client/           ← Razorshave-Projekt (dieses wird transpiliert)
│   ├── Pages/              ← @page Components
│   ├── Components/         ← Shared Components
│   ├── Services/           ← [Client] Services
│   ├── Shared/             ← Layouts, NavMenu
│   └── Program.cs          ← Blazor Server Host für Dev
│
└── MyApp.Contracts/        ← optional: Shared DTOs, [ApiRoute] Interfaces
    ├── Dtos/
    └── IUserApi.cs
```

**Dev-Flow:**
- User startet `MyApp.Server` (ASP.NET Core) auf z.B. `https://localhost:5001`
- User startet `MyApp.Client` (Blazor Server) auf z.B. `https://localhost:5002`
- Client macht echte HTTP-Calls gegen Server — genau wie Production

**Build-Flow:**
- `razorshave build` in `MyApp.Client/` → produziert `dist/`
- `dist/` wird auf nginx deployed, redet via HTTP mit `MyApp.Server`

Dev und Build unterscheiden sich **nicht** in der Daten-Schicht.

---

## Template-Setup: "dotnet new blazor" + 1 Zeile

Ein zentrales Design-Ziel von Razorshave ist **null Invasivität**. Das minimale Blazor-Template läuft unverändert durch Razorshave — mit genau einer zusätzlichen Zeile.

### Was User im Template anpasst

**Program.cs** — fügt eine Zeile hinzu:

```csharp
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();    // bleibt unverändert

builder.Services.AddRazorshave();          // NEU: Razorshave-Services registrieren
```

**Das war's.** Kein Entfernen, kein Umbauen, kein Conditional-Code.

### Was bleibt unverändert

- `App.razor` — keine Änderung
- `Routes.razor` — keine Änderung, auch nicht das `<Router>`-Markup (Transpiler handled das build-time)
- `Home.razor` — keine Änderung
- `Counter.razor` — keine Änderung, `@rendermode="InteractiveServer"` bleibt drin und wird im Build ignoriert
- `NavMenu.razor` — keine Änderung, `<NavLink>` ist in Razorshave-Runtime nachgebaut
- `MainLayout.razor` — keine Änderung

### Was User anpasst wenn er Features nutzt

**Weather.razor** (oder eigene Datenzugriff-Components) — von In-Memory-Mock zu ApiClient-Pattern umgeschrieben:

```csharp
// Original (läuft nicht im Build, weil kein echter Datenzugriff):
@code {
    private WeatherForecast[]? forecasts;
    protected override async Task OnInitializedAsync() {
        await Task.Delay(500);
        forecasts = Enumerable.Range(1, 5).Select(i => new WeatherForecast {...}).ToArray();
    }
}

// Razorshave-konform (läuft in Dev UND Build):
@inject IWeatherApi WeatherApi

@code {
    private WeatherForecast[]? forecasts;
    protected override async Task OnInitializedAsync() {
        forecasts = await WeatherApi.GetForecastsAsync();
    }
}
```

Plus `IWeatherApi` Interface mit `[ApiRoute]` und Server-seitiger Implementation.

### Wieso das Template so wenig Anpassung braucht

- `@rendermode`-Direktiven werden vom Transpiler ignoriert (nur im Dev aktiv)
- `AddInteractiveServerComponents()` und andere Blazor-Server-Infrastruktur werden vom Transpiler ignoriert (Program.cs wird nicht transpiliert)
- `<Router>` wird vom Transpiler erkannt und build-time durch Razorshave-Router mit statischer Route-Table ersetzt
- `NavLink`, `RouteView`, `CascadingValue`, andere Blazor-Built-in-Components sind in der Razorshave-Runtime als Components nachgebaut

User schreibt Blazor. User baut mit Razorshave. Fertig.

---

## ApiClient-Basis-API

### Interface

```csharp
namespace Razorshave.Abstractions;

public abstract class ApiClient
{
    protected ApiClient(HttpClient httpClient) { /* ... */ }
    
    protected Task<T> Get<T>(string path);
    protected Task<T> Post<T>(string path, object? body = null);
    protected Task<T> Put<T>(string path, object? body = null);
    protected Task Delete(string path);
    
    // Interceptor-Pattern
    protected virtual Task ConfigureRequestAsync(ApiRequest request) 
        => Task.CompletedTask;
    
    protected virtual Task HandleResponseAsync(ApiResponse response) 
        => Task.CompletedTask;
}

public class ApiRequest
{
    public string Method { get; }
    public string Path { get; }
    public Dictionary<string, string> Headers { get; } = new();
    public object? Body { get; set; }
}

public class ApiResponse
{
    public int StatusCode { get; }
    public Dictionary<string, string> Headers { get; }
    public string? Body { get; }
}
```

### Header-Konfiguration & Response-Handling

`ConfigureRequestAsync` wird vor jedem Request aufgerufen — beliebig viele Header setzbar, Body modifizierbar.

`HandleResponseAsync` wird nach jedem Response aufgerufen — für Logging, Token-Refresh, globale 401-Behandlung, etc.

```csharp
[Client]
public class MyAuthenticatedApi : ApiClient
{
    private readonly IStore<AuthState> _auth;
    private readonly IStore<TenantState> _tenant;
    
    public MyAuthenticatedApi(HttpClient http, IStore<AuthState> auth, IStore<TenantState> tenant) 
        : base(http) { _auth = auth; _tenant = tenant; }
    
    protected override Task ConfigureRequestAsync(ApiRequest req)
    {
        var token = _auth.Get("current")?.Token;
        if (token != null)
            req.Headers["Authorization"] = $"Bearer {token}";
        
        req.Headers["X-Tenant-Id"] = _tenant.Get("active")?.Id ?? "";
        req.Headers["Accept-Language"] = CultureInfo.CurrentUICulture.Name;
        return Task.CompletedTask;
    }
    
    protected override Task HandleResponseAsync(ApiResponse res)
    {
        if (res.StatusCode == 401) 
            _auth.Delete("current"); // force re-login
        return Task.CompletedTask;
    }
}
```

Andere ApiClient-Subclasses erben von diesem → Header-Pipeline wird automatisch angewendet.

---

## ApiClient-Pattern

Der einzige Weg zu Daten aus einer Razorshave-Component: **ApiClient-basierte Services**.

### Option A: Manuelle Implementation

User schreibt die Client-Klasse selbst:

```csharp
[Client]
public class UserApi : ApiClient
{
    public UserApi(HttpClient http) : base(http) { }
    
    public Task<User[]> GetAllAsync() 
        => Get<User[]>("api/users");
    
    public Task<User> GetByIdAsync(int id) 
        => Get<User>($"api/users/{id}");
    
    public Task<User> CreateAsync(User user) 
        => Post<User>("api/users", user);
    
    public Task DeleteAsync(int id) 
        => Delete($"api/users/{id}");
}
```

Explizit, debugbar, keine Magic.

### Option B: Auto-Generated via [ApiRoute]

User deklariert nur ein Interface:

```csharp
[ApiRoute("api/users")]
public interface IUserApi
{
    [Get] Task<User[]> GetAllAsync();
    [Get("{id}")] Task<User> GetByIdAsync(int id);
    [Post] Task<User> CreateAsync(User user);
    [Delete("{id}")] Task DeleteAsync(int id);
}
```

Ein Roslyn Source-Generator erzeugt zur Build-Zeit eine Implementation: `_UserApi_Generated` die von `ApiClient` erbt und `IUserApi` implementiert.

Diese wird automatisch in beiden Welten genutzt:
- **Dev:** im Blazor Server DI-Container registriert
- **Build:** im Razorshave-Runtime-Container registriert, transpiliert zu fetch-Calls

### Beide Optionen erlaubt

User kann je Service entscheiden welcher Stil besser passt. `[ApiRoute]` für Standard-CRUD, manuelle Klasse für Custom-Logik, Error-Handling, File-Uploads etc.

### Attribute-Referenz

```csharp
[ApiRoute("api/users")]              // Basis-Route für alle Methoden
public interface IUserApi
{
    [Get]                            // GET api/users
    [Get("{id}")]                    // GET api/users/{id}
    [Get("active")]                  // GET api/users/active
    
    [Post]                           // POST api/users (Body = erster Parameter)
    [Put("{id}")]                    // PUT api/users/{id}
    [Delete("{id}")]                 // DELETE api/users/{id}
    
    // Query-Parameter via Method-Parameter-Name
    [Get("search")]
    Task<User[]> SearchAsync(string query, int limit);
    // → GET api/users/search?query=...&limit=...
}
```

---

## Validation-Modell: Allowlist

**Prinzip:** Wir prüfen ob User ausschließlich Razorshave-Ökosystem-Zeug nutzt. Alles was wir nicht kennen → Build-Fail.

Keine Blacklist (Pflegeaufwand, neue Versionen brechen). Stattdessen: explizite Allowlist dessen was **transpilierbar** oder **zulässig** ist.

### Allowlist-Kategorien

**1. Razorshave-Ökosystem** (immer OK)
- `Razorshave.Abstractions.*` — `[Client]`, `[ApiRoute]`, `IStore<T>`, `ApiClient`, HTTP-Verb-Attribute
- `Razorshave.Runtime.*` — Runtime-Types die der Transpiler nutzt

**2. Blazor Component-Infrastruktur** (transpilierbar)
- `Microsoft.AspNetCore.Components.ComponentBase`
- `Microsoft.AspNetCore.Components.Parameter`, `CascadingParameter`
- `Microsoft.AspNetCore.Components.NavigationManager`
- `Microsoft.AspNetCore.Components.Routing.*` (NavLink, Router)
- `Microsoft.AspNetCore.Components.Web.*` (EventArgs, PageTitle, HeadContent)
- `Microsoft.AspNetCore.Components.CascadingValue<T>`
- `Microsoft.AspNetCore.Components.ErrorBoundary`
- `Microsoft.JSInterop.IJSRuntime`, `IJSObjectReference`
- `EventCallback`, `EventCallback<T>`, `RenderFragment`, `RenderFragment<T>`

**3. BCL-Subset** (transpilierbar, konsultiere Schlachtplan für Details)
- `System` — primitives, Math, Convert, Exception-Types
- `System.Collections.Generic` — List, Dictionary, HashSet, Queue, Stack
- `System.Linq.Enumerable` — Subset aus Schlachtplan
- `System.Text.StringBuilder`, `System.Text.RegularExpressions`
- `System.Threading.Tasks` — Task, Task<T>, ValueTask<T>
- `Microsoft.Extensions.Logging.ILogger<T>` — transpiliert zu `console.log/warn/error`

**Explizit NICHT in BCL-Subset:**
- `System.Net.Http.HttpClient` — direkter Zugriff ist Analyzer-Error. Nur über ApiClient nutzbar (der intern HttpClient wrappt im Dev und fetch im Build).

**4. User-definierte Types** (automatisch OK)
- Alles in Razorshave-Projekt + alle `ProjectReference`-Projekte wird transpiliert
- Folgt selbst den Allowlist-Regeln für die verwendeten APIs
- User entscheidet selbst ob DTOs/Contracts in eigenem Shared-Projekt oder inline liegen

**5. Known-Good NuGet-Packages** (geprüfte Liste)
- Liste startet klein, wächst mit Tests
- Beispiel-Kandidaten: `System.Text.Json` (nach Verifikation), ausgewählte Small-Utility-Packages

**6. Shared-Projects mit Source** (automatisch)

Jedes `<ProjectReference>` im User-csproj wird rekursiv eingelesen und transpiliert. Der typische Use-Case ist ein `MyApp.Contracts` Projekt mit DTOs und `[ApiRoute]`-Interfaces — das funktioniert automatisch, kein opt-in nötig.

**Nicht möglich:** Externe NuGet-Packages (DLLs ohne Source) können niemals transpiliert werden. Per Design. Wer einen externen Package-Inhalt im Razorshave-Projekt braucht, muss entweder:
- Das Package als ProjectReference einbinden (wenn Open-Source verfügbar)
- Den benötigten Code selbst in seinen Code übernehmen
- Einen serverseitigen Proxy-Endpoint bauen der das Feature über die API zugänglich macht

### Was passiert bei Build-Fail

User nutzt `@inject DbContext` in einer Component:

```
RZS1001: Symbol 'Microsoft.EntityFrameworkCore.DbContext' is not in the Razorshave ecosystem.
  at UserList.razor:5
  
Razorshave transpiles to JavaScript and runs in the browser. Types from 
server-only packages (EF Core, ASP.NET Core MVC, System.IO, etc.) are 
not available.
  
Use the ApiClient pattern to access data:
  
  [ApiRoute("api/users")]
  public interface IUserApi { [Get] Task<User[]> GetAllAsync(); }
  
  @inject IUserApi UserApi
```

Klare Message, klarer Fix, kein Raten.

### Was der Check konkret macht

```
Für jedes Symbol im User-Code:
  semanticModel.GetSymbolInfo(node).Symbol
  
  1. symbol.ContainingAssembly == Razorshave.Abstractions?     → OK
  2. symbol.ContainingAssembly == Razorshave.Runtime?          → OK
  3. symbol.ContainingNamespace in SupportedBlazorNamespaces?  → OK
  4. symbol.ContainingNamespace in SupportedBclNamespaces?     → OK
  5. symbol.ContainingAssembly == UserProject?                 → OK (recurse)
  6. symbol.ContainingAssembly in KnownGoodPackages?           → OK
  7. symbol.ContainingAssembly in UserAllowedPackages?         → OK (with warning)
  8. else                                                      → ERROR
```

Der Check läuft zweifach:
- **Analyzer** (IDE): als Warnings/Errors in Visual Studio/Rider während des Schreibens
- **Transpiler** (Build): als harter Build-Fail

---

## Dev-Mode Details

Im Dev läuft der User im Blazor Server mit:
- Echter Hot Reload (Microsoft's native .NET Hot Reload)
- Voller Debugger, Breakpoints in C#
- Alle Razorshave-Regeln werden vom **Analyzer** als Warnings gezeigt, aber **nicht als Runtime-Errors**

**Das heißt:** Wenn User im Dev `@inject DbContext` schreibt, läuft es im Blazor Server Dev-Mode. Der Analyzer zeigt einen Fehler. Beim `razorshave build` fails der Build.

User kann also **theoretisch** Razorshave-incompatiblen Code im Dev schreiben, aber er kriegt IDE-Warnings und der Build wird failen. Keine Überraschung, klares Feedback-Loop.

---

## Entschieden

- **HttpClient direkt nutzen:** nein, Analyzer-Error. Nur über ApiClient.
- **Contracts-Lokalisierung:** egal, User entscheidet. Wir scannen ProjectReferences rekursiv.
- **Source-Generator für [ApiRoute]:** Build-Time. Funktioniert in Dev (Blazor Server Build) und im Razorshave-Build identisch.
- **Known-Good-Liste:** zentral im Razorshave-Repo, JSON-File, mit jedem Razorshave-Release ausgeliefert. Community-PRs willkommen.
- **User-allowed Packages:** erlaubt mit `acknowledgedRisk: true`. Transpiler versucht best-effort, gibt bei Fail klare Messages welcher Typ das Problem ist.
- **ILogger<T>:** transpiliert zu `console.log/warn/error`.

## Noch zu klären

_Keine offenen Punkte für diese Sub-Section._

---

## Zusammenfassung

**Was User schreibt:**
- Components in `.razor` (ganz normal)
- Services mit `[Client]`
- Interfaces mit `[ApiRoute]` oder manuelle ApiClient-Subclasses
- Stores via `IStore<T>` (injiziert, nicht selbst geschrieben)

**Was User NICHT schreibt:**
- Keine Conditional-Compilation
- Keine Dual-Implementations
- Keine zweite Client-Welt
- Keine DB-Access-Code im Razorshave-Projekt

**Was Razorshave macht:**
- Prüft Allowlist-strict via Analyzer + Transpiler
- Baut den Client-Code zu statischem JS
- Generiert ApiClient-Implementations aus Interfaces
