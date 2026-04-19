# Razorshave: Elevator Pitch

> Die kompakte Erklärung was Razorshave ist. Für Kollegen, Fachgespräche, Dev-Community.

**Status:** Final
**Parent:** RAZORSHAVE.md
**Datum:** 2026-04-19

---

## Pitch

> Kennst du das Problem — du willst was in .NET bauen, aber als SPA deployen mit nginx? Blazor WASM ist fett, Blazor Server braucht immer eine Verbindung, und alles andere heißt React lernen.
>
> Razorshave ist ein Build-Tool das Blazor-Code zu statischem JavaScript kompiliert. Du schreibst ganz normal `dotnet new blazor`, fügst eine Zeile hinzu, `dotnet build` und kriegst ein `dist/` raus das auf nginx läuft. Kein WASM-Runtime, kein Server. Einfach JavaScript.
>
> Im Dev debugst du ganz normal in Visual Studio mit Blazor Server. Im Build wird's zum SPA. C# end-to-end, JavaScript-UI-Libraries wie Chart.js oder AG Grid per Interop, deploybar wie jede andere SPA.

---

## Kurz-Varianten

### 10-Sekunden-Version (Ein-Satz-Pitch)

Razorshave kompiliert Blazor-Code zu statischem JavaScript — keine WASM-Runtime, kein Server, deploybar wie jede SPA.

### 3-Sekunden-Version (Slogan)

Write Blazor. Deploy as SPA.

---

## Elevator Pitch in English (Dev-Twitter-Version)

> Write Blazor. Deploy as SPA. Razorshave is a build-time compiler that turns your Blazor components into static JavaScript — no WASM, no SignalR, no runtime overhead. Debug in Visual Studio with Blazor Server during dev, ship a static bundle to nginx in production. C# end-to-end, JS-library interop when you need it.

---

## Kontext

**Warum existiert Razorshave?**

- Blazor WASM: zu fettes Initial-Bundle (mehrere MB), zu langsamer First-Paint
- Blazor Server: permanente WebSocket-Connection, nicht deploy-to-static
- React/Vue/Angular: komplett anderer Stack, neue Tool-Chain, neue Skills

.NET-Entwickler die SPAs bauen wollen hatten bisher keine saubere Option die (a) in .NET bleibt und (b) wie moderne SPAs deploybar ist. Razorshave füllt diese Lücke.

**Wer ist Zielgruppe?**

- .NET-Teams die SPAs bauen wollen ohne React-Stack einzuführen
- Einzelne .NET-Entwickler die Side-Projects schnell deploybar haben wollen
- Teams die ihre Blazor-Erfahrung wiederverwenden möchten für client-heavy Apps

**Was Razorshave nicht ist:**

- Kein Ersatz für Blazor Server bei Apps mit echter Server-Render-Logik
- Kein SSR/SEO-Tool (reine Client-Side-SPAs)
- Kein MAUI-Ersatz (kein Mobile/Desktop-Targeting)

---

## Differenzierung

| | Blazor WASM | Blazor Server | Razorshave | React/Vue |
|---|---|---|---|---|
| Sprache | C# | C# | C# | JS/TS |
| Bundle-Size | 2-5 MB | klein | 150-250 KB | 200-500 KB |
| Initial Load | langsam | schnell | schnell | mittel |
| Server-Requirement | nein | ja (persistent) | nein | nein |
| Deploy | static | ASP.NET host | static (nginx, S3) | static |
| Runtime im Browser | .NET WASM | keine (server-render) | eigene JS-Runtime | React/Vue-Runtime |
| IDE-Experience | Visual Studio | Visual Studio | Visual Studio | VSCode/WebStorm |
| JS-Library-Interop | über `IJSRuntime` | über `IJSRuntime` | über `IJSRuntime` | nativ |
