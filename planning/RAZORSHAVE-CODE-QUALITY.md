# Razorshave: Code Quality & Guidelines

> Die Regeln die über saubere vs. spaghettige Code-Basis entscheiden.

**Status:** Design-Draft
**Parent:** RAZORSHAVE.md
**Datum:** 2026-04-19

---

## Kern-Prinzipien (die die Struktur retten)

Drei Regeln die wichtiger sind als alles andere:

### 1. Saubere Abstraktion — alles ist mockbar

Jede Klasse die externe Abhängigkeiten nutzt (File-I/O, HTTP, Roslyn-Workspace, MSBuild-API, esbuild-Process) muss ihre Dependencies über Interfaces bekommen. Nicht direkt instanziieren.

**Gut:**
```csharp
public class Transpiler
{
    private readonly IFileReader _files;
    private readonly ISymbolResolver _symbols;
    
    public Transpiler(IFileReader files, ISymbolResolver symbols) { ... }
}
```

**Schlecht:**
```csharp
public class Transpiler
{
    public string Transpile(string path) {
        var content = File.ReadAllText(path);  // Direkte Kopplung, nicht mockbar
        ...
    }
}
```

Grund: Tests müssen Dependencies stubben können. Wenn Abstraktion fehlt, braucht jeder Test ein echtes Filesystem, echten MSBuild, echten Process-Call — Tests werden langsam, fragil, unwartbar.

### 2. Keine Spaghetti-Methoden oder -Klassen

Eine Methode macht eine Sache. Ein Statement hat einen klaren Zweck. Wenn eine Methode mehr als ~30 Zeilen hat oder mehr als 3 Ebenen Verschachtelung, wird sie aufgesplittet.

Eine Klasse hat einen fokussierten Zweck. Wenn eine Klasse über 300 Zeilen wächst oder mehrere unabhängige Verantwortlichkeiten trägt, wird sie aufgeteilt.

**Sign of spaghetti:**
- Methode braucht 5+ Parameter
- `if`-Ketten tiefer als 3 Ebenen  
- Klassenname endet auf "Manager", "Helper", "Util" ohne klaren Fokus
- Private Felder die in unterschiedlichen Methoden unterschiedliche Rollen spielen

### 3. Single Responsibility

Jede Klasse, jede Methode, jedes Modul hat **einen** Grund zu existieren, **einen** Grund sich zu ändern.

Transpiler-Beispiel:
- `ExpressionEmitter` emittiert Expressions. Nichts anderes.
- `StatementEmitter` emittiert Statements. Nichts anderes.
- `ClassEmitter` orchestriert Class-Declaration-Emission. Nutzt die anderen zwei.

Kein `UniversalTranspiler`-Gott-Objekt das alles macht.

---

## Sprache & Konventionen

### C# (Transpiler, Analyzer, Source-Generator, MSBuild-Task)

Standard-.NET-Konventionen. Ohne Abweichung.

- PascalCase für Public Members, Types, Methods
- camelCase für lokale Variablen, Parameter
- `_camelCase` für private Fields
- `I` Präfix für Interfaces
- Async-Methoden enden auf `Async`
- `.editorconfig` committed (Microsoft-Default-Rules)
- Nullable Reference Types aktiviert projektweit
- `file-scoped namespace` als Default

### JavaScript (Runtime)

**Pure JavaScript. Kein TypeScript.**

- ESLint mit simpler Config (keine Airbnb-Overkill, nur sinnvolle Basics)
- camelCase für alles außer Klassen (die sind PascalCase)
- ES2022+ Features nutzen (Classes, `#private`, `??`, `?.`, async/await)
- ESM-Module (`import/export`)
- JSDoc-Comments an API-Grenzen (was ist Public API, was sind Parameter-Typen)
- Keine Transpilation-Pipeline für die Runtime selbst — der Code ist schon lesbar

### CSS / HTML

In Razorshave's Output: was der Microsoft-Compiler erzeugt bleibt wie es ist. Wir adden nichts eigenes.

---

## Code-Struktur

### Klassengrößen-Richtlinie

- **Unter 100 Zeilen:** ideal
- **100-300 Zeilen:** okay bei klarer Verantwortung
- **Über 300 Zeilen:** Review-Pflicht, meist Aufteilung nötig

### Methodengrößen-Richtlinie

- **Unter 15 Zeilen:** ideal  
- **15-30 Zeilen:** okay bei klarer Logik
- **Über 30 Zeilen:** Review-Pflicht, meist Aufteilung nötig

### Verschachtelungs-Grenze

Maximal 3 Ebenen Indentation in einer Methode. Tiefer = Guard-Clauses oder Extract-Method einsetzen.

### Parameter-Anzahl

Maximal 4 Parameter pro Methode. Bei mehr: Parameter-Object einführen.

---

## Testing-Enforcement

Die Code-Quality-Regeln werden durch die Test-Strategie **erzwungen**:

- 95%+ Coverage-Ziel auf Transpiler/Analyzer/Source-Generator zwingt zu mockbarer Abstraktion. Nicht-mockbare Code ist nicht testbar ist nicht coverable.
- TDD schärft Single-Responsibility — Tests schreiben zuerst fordert klare Verantwortlichkeiten.
- Snapshot-Tests decken Spaghetti-Refactorings auf — wenn ein Transpiler-Change 50 Snapshots ändert, ist die Abstraktion kaputt.

---

## Review-Prozess

**Interaktiv nach jedem Consors-Durchlauf.**

Flow:
1. Consors bekommt Task, arbeitet
2. Consors liefert: Code + Tests + kurze Beschreibung was gemacht wurde
3. Bernhard reviewed: Code lesen, Tests nachvollziehen, bei Unklarheit nachfragen
4. Freigabe oder Nachbesserung
5. Nächster Task

Kein PR-Workflow im GitHub-Sinne (wäre Overhead für Abend-Projekt mit einem Human). Aber auch kein Blind-Merge — jeder Schritt wird gesichtet bevor der nächste losgeht.

---

## Error-Handling

### User-facing Errors (aus Analyzer, Transpiler, MSBuild-Task)

- Immer RZS-Code (z.B. `RZS1001`)
- Immer Message mit klarer Beschreibung was schiefgelaufen ist
- Immer Location (File + Zeile, wenn möglich)
- Immer Fix-Hinweis: "Um das zu beheben, ..."

Beispiel:
```
RZS1001: Symbol 'Microsoft.EntityFrameworkCore.DbContext' is not in the 
         Razorshave ecosystem.
  at UserList.razor:5
  
Razorshave transpiles to JavaScript. Server-only types are not available.
Use the ApiClient pattern instead:

  [ApiRoute("api/users")]
  public interface IUserApi { [Get] Task<User[]> GetAllAsync(); }
```

### Interne Errors (Transpiler-Bugs, Runtime-Bugs)

- Exceptions throwen mit Stacktrace
- Kein Swallowing, kein `catch { }`
- Fail fast — lieber crashen als unsauber weitermachen

### Result<T>-Pattern wird nicht erzwungen

Wir nutzen Exceptions als primären Error-Mechanismus (.NET-Style). Result<T> als Pattern ist funktional sexy, aber zwingt User der API in eine bestimmte Denkweise. Nicht der Hügel auf dem wir sterben wollen.

---

## Dependency-Policy

**Minimal.** Jede Dependency muss gerechtfertigt sein.

### C# Transpiler

Erlaubt:
- `Microsoft.CodeAnalysis.CSharp` (Roslyn) — Kern
- `Microsoft.Build.*` (MSBuild-API) — Kern
- `Verify.Xunit` (nur in Test-Projekten) — Snapshot-Tests
- `xunit` (nur in Test-Projekten)

Nicht erlaubt ohne Review:
- Jede andere NuGet-Dependency

### Razorshave.Runtime (JavaScript)

Erlaubt:
- `decimal.js-light` — eingebettet, nicht als npm-Dependency
- `vitest` (nur Dev-Dependency für Tests)

Nicht erlaubt:
- **Keine Runtime-Dependencies** außer decimal.js-light
- Kein React, Vue, Preact, Redux, etc. — wir sind selbst ein Framework, nicht eine App
- Keine Utility-Libraries wie Lodash — wir schreiben was wir brauchen selbst

### Razorshave.Abstractions

- Keine Dependencies. Das ist ein reines Interface/Attribute-Package.

---

## Performance-Priorität

**Reihenfolge:** Korrektheit > Lesbarkeit > Performance.

In v0.1 wird **nicht optimiert** außer die Performance ist erkennbar problematisch:

- Kitchen-Sink-Build dauert länger als 30 Sekunden → untersuchen
- Bundle-Size größer als 400 KB gzipped → untersuchen
- Runtime rendert Counter-Klick spürbar verzögert → untersuchen

Ohne konkretes Problem: keine Optimierung. Lieber lesbarer Code.

**Benchmark-Fixtures** werden später angelegt (v0.2) um Regression zu erkennen. Jetzt nicht.

---

## Dokumentations-Standard

### Public API

Alle Public Classes, Methods, Properties haben XML-Doc-Comments. Das sind die Typen die in `Razorshave.Abstractions` exposed sind und User in ihrem Code sehen.

```csharp
/// <summary>
/// Attribute markiert eine Klasse als Client-Service der im Razorshave-Container 
/// registriert und in Components injiziert werden kann.
/// </summary>
public class ClientAttribute : Attribute { }
```

### Interne Klassen

Nur kommentieren wenn es nicht offensichtlich ist **warum** etwas so gemacht wird. Das "was" liest man im Code.

**Gut:**
```csharp
// Sequence-Numbers kommen aus Blazor's Server-Render-Diff-Optimierung.
// Für unseren Client-VDOM sind sie irrelevant — einfach ignorieren.
var _ = node.GetFirstArgument();
```

**Schlecht:**
```csharp
// Gets the first argument
var firstArg = node.GetFirstArgument();
```

### Architektur-Entscheidungen

Leben in den Sub-Docs (RAZORSHAVE-*.md). Keine separaten ADR-Files. Die Sub-Docs sind schon strukturiert genug.

---

## Git & Commit-Messages

**Freier Stil.** Kein Conventional-Commits-Zwang. Wichtig ist nur:

- Commits beschreiben **was** geändert wurde und **warum** (wenn nicht offensichtlich)
- Keine "wip" oder "fix typo" Commits auf main — squashen vor Merge
- Branch-Namen sind aussagekräftig (`feature/transpile-foreach`, `fix/router-back-button`)

---

## Versionierung

Semantic Versioning. Aber interner Gebrauch bis v1.0:

- Pre-M0: `0.0.x`
- M0 erreicht: `0.1.0`
- Feature-additions v0.1: `0.1.x`
- Breaking-Changes vor v1.0 erlaubt — erst v1.0 gibt Stability-Guarantee

---

## Zusammenfassung

Die **drei entscheidenden Regeln**, auf die alles andere aufbaut:

1. **Saubere Abstraktion** — alles ist mockbar
2. **Keine Spaghetti** — klare, fokussierte Methoden und Klassen  
3. **Single Responsibility** — jede Einheit hat genau einen Grund zu existieren

Wenn diese drei konsequent eingehalten werden, passen alle anderen Regeln (Testing, Error-Handling, Code-Struktur) fast von allein.
