# Razorshave — Quality Backlog

Tests: **191 JS + 104 C# = 295 grün.**

---

## 🔵 Nicht systematisch auditiert

### Transpiler-Korrektheit (Rest)

- [ ] `@bind-get`/`@bind-set` Transform-Variante — keine real-world-Fundstelle, wird mit erstem Testcase angegangen
- [ ] Partial classes über mehrere `[Client]`-Files (rare; Razor-SourceGen merged Razor+Code-behind automatisch)

### Ungetestete C#-Constructs (Analyzer flaggt; Transpiler-Support optional)

Allowlist wächst mit Emitter-Cases. Analyzer fängt alles hier am Dev-Build.

- [ ] `is Type`, `is Type t`, Property-/Tuple-Pattern (größerer Brocken, eigenes Ticket)
- [ ] `switch`-STATEMENT (Expression ist supported)
- [ ] Tuple-Literale + Deconstruction
- [ ] `using`-statement
- [ ] Local functions, `ref`/`out`, `yield`, `try/catch/finally`, `throw`-expr

### BCL-Rewrites (Rest)

- [ ] `TimeSpan`, Date-Arithmetic (addDays/subtract)
- [ ] LINQ: `.OrderBy/.OrderByDescending` (braucht Comparator)
- [ ] `Dictionary<K,V>` / `HashSet<T>` — JS Map/Set sind passable Ersatz, aber Emitter fehlt

### Reconciler Transition-Matrix (Rest)

- [ ] tiefe component→component verschiedener Ctor mit shared children (state-Preservation)
- [ ] keyed-list type-switching

---

## 🧪 Test-Systematik

- [ ] **Feature-Matrix**: ein Test pro C#-Construct + BCL-Rewrite
- [ ] **Dev-Host-Parität**: Blazor-Server startet in-process, HTML-Output gegen transpiled-SPA-Output vergleichen
- [ ] **Razorshave.Abstractions `AddRazorshave()`** DI-resolve-Tests (Storage/Cookie jetzt abgedeckt)
- [ ] **Fuzzing/Property-Tests** gegen Transpiler (random C# → parse as valid JS via Acorn)

---

## 📋 Meta / Tooling

- [ ] `SUPPORTED.md` — Liste was explizit NICHT supported ist
- [ ] `examples/` — Mini-Demos pro Feature (Store, Storage, Router, ApiClient, Bind, Keyed-List)

---

## 🧱 Next-Level Features

- [ ] Source-Maps (Stack-Trace auf .razor/.cs)
- [ ] `CascadingValue` / `CascadingParameter`
- [ ] `ErrorBoundary` Component
- [ ] `@ref` auf Elements + Components
- [ ] Virtualization für lange Listen
- [ ] Fehlende EventArgs (Drag/Wheel/Clipboard/Touch) — Keyboard + Mouse jetzt komplett
