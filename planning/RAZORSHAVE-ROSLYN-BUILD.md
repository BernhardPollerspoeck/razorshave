# Razorshave: Roslyn-Integration & Build-Flow

> Wie Razorshave sich in den .NET-Build-Prozess einhängt, was passiert wenn User `dotnet build` ruft, und wie der Transpiler mit Roslyn arbeitet.

**Status:** Design-Draft
**Parent:** RAZORSHAVE.md
**Datum:** 2026-04-19

---

## Kern-Prinzip

Razorshave ist ein **Post-Compile-Transpiler**, kein Parallel-Compiler. Wir lassen MSBuild das tun was MSBuild kann (C# parsen, Razor kompilieren, Source-Generators ausführen, NuGet auflösen) und transpilieren anschließend das Ergebnis zu JavaScript.

**Wir transpilieren nur Source-Code. Niemals DLLs.** Externe NuGet-Packages ohne Source können nicht transpiliert werden — das ist per Design.

---

## Build-Flow

```
1. User ruft:           dotnet build -c Razorshave
   ↓
2. MSBuild läuft normal:
   - C# Compiler        → MyApp.Client.dll (wird nie deployed, aber generiert)
   - Razor Source-Gen   → obj/.../generated/.../Counter.razor.g.cs
   - Razorshave Source-Gen → obj/.../generated/.../_UserApi_Generated.cs
   - Scoped CSS         → obj/.../scopedcss/projectbundle/MyApp.Client.styles.css
   ↓
3. MSBuild-Target "Razorshave" triggert nach Build (via .targets-File)
   ↓
4. Razorshave sammelt:
   - Alle .cs aus User-Projekt + ProjectReferences (rekursiv)
   - Alle .cs aus obj/.../generated/ (Source-Gen-Output)
   - Scoped CSS bundle aus obj/.../scopedcss/
   - wwwroot/ Ordner
   ↓
5. Razorshave baut Roslyn-Compilation aus allen Sources
   ↓
6. Validation: Allowlist-Check auf jedes Symbol
   - Fail → harter Stop mit RZS-Error-Code + File/Zeile
   ↓
7. Transpilation: C# → JS
   - User-Code via SyntaxWalker + JS-Emitter
   - Generated Code via gleichem Walker
   - RenderTreeBuilder-Calls via Spezial-Walker → h()-Calls
   ↓
8. Runtime-Injection: @razorshave/runtime Module-Imports einfügen
   ↓
9. Bundling: esbuild bundelt + minified + tree-shakes
   ↓
10. Output: dist/ 
    ├── index.html
    ├── app.[hash].js
    ├── app.[hash].css
    └── assets/ (aus wwwroot 1:1 kopiert)
```

---

## MSBuild-Integration

Razorshave integriert sich als **MSBuild-Target**, nicht als Standalone-CLI. User workflow bleibt `dotnet build`.

### Razorshave.Cli NuGet-Package enthält

- Das ausführbare Razorshave-Transpiler-Binary
- Ein `build/Razorshave.Cli.targets` File das MSBuild automatisch lädt
- Eine `build/Razorshave.Cli.props` mit Default-Properties

### Targets-File (Skeleton)

```xml
<Project>
  <!-- Razorshave-Target läuft nach Build -->
  <Target Name="RazorshaveTranspile" 
          AfterTargets="Build"
          Condition="'$(Configuration)' == 'Razorshave'">
    
    <Exec Command="razorshave-transpile 
                   --project=$(MSBuildProjectFullPath)
                   --intermediate=$(IntermediateOutputPath)
                   --output=$(RazorshaveOutputPath)
                   --target-framework=$(TargetFramework)" />
  </Target>
  
  <!-- Default Output-Pfad -->
  <PropertyGroup>
    <RazorshaveOutputPath Condition="'$(RazorshaveOutputPath)' == ''">dist/</RazorshaveOutputPath>
  </PropertyGroup>
</Project>
```

### Usage

```bash
# Build mit Razorshave-Transpilation
dotnet build -c Razorshave

# Output landet in: MyApp.Client/dist/
```

---

## Input-Sammlung

Nach erfolgreichem Build sammelt Razorshave:

### 1. User-Source

- Alle `.cs` Files im Projekt-Ordner (exklusive `bin/`, `obj/`)
- Alle `.razor` Files werden **nicht direkt** gelesen — deren Compile-Output (`.razor.g.cs`) wird verwendet

### 2. Generated Sources

- `obj/$(IntermediateOutputPath)/generated/` rekursiv
- Enthält:
  - Razor-Compiler-Output (`*.razor.g.cs`)
  - Razorshave-ApiRoute-Source-Generator-Output
  - Ggf. andere User-aktive Source-Generators

### 3. ProjectReference-Sources

- Jedes referenzierte Projekt wird rekursiv geöffnet
- Dessen `.cs` + generated Sources werden mit eingelesen
- Beispiel: `MyApp.Contracts` mit DTOs und `[ApiRoute]`-Interfaces

### 4. CSS-Bundle

- `obj/.../scopedcss/projectbundle/$(AssemblyName).styles.css`
- Wird 1:1 ins `dist/` kopiert mit Content-Hash

### 5. Static Assets

- `wwwroot/` Ordner rekursiv
- 1:1 ins `dist/assets/` kopiert

---

## Assembly-Referenzen (für Semantic-Analyse)

Roslyn's `SemanticModel` braucht alle referenzierten Assemblies um Types aufzulösen. Auch wenn wir die Assemblies nicht transpilieren, brauchen wir sie um zu verstehen was User-Code **tut**.

Beispiel: `[Client] public class Foo` — Roslyn muss `ClientAttribute` in `Razorshave.Abstractions.dll` finden um den Attribute-Namen korrekt aufzulösen.

Wir bekommen alle References von MSBuild über den `ReferencePath`-ItemGroup, die MSBuild bei NuGet-Restore bereits aufgelöst hat. Keine manuelle NuGet-Logik nötig.

---

## Compilation-Aufbau

```csharp
// Pseudo-Code

// 1. Alle Sources als SyntaxTrees parsen
var syntaxTrees = allSourceFiles
    .Select(path => CSharpSyntaxTree.ParseText(
        File.ReadAllText(path),
        path: path))
    .ToList();

// 2. Assembly-References von MSBuild
var references = msbuildReferencePaths
    .Select(path => MetadataReference.CreateFromFile(path))
    .ToList();

// 3. Compilation bauen
var compilation = CSharpCompilation.Create(
    assemblyName: projectName,
    syntaxTrees: syntaxTrees,
    references: references,
    options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

// 4. Semantic Model pro Tree für Symbol-Resolution
foreach (var tree in syntaxTrees) {
    var semanticModel = compilation.GetSemanticModel(tree);
    // → ValidationPass, TranspilationPass nutzen semanticModel
}
```

---

## Validation-Pass

Walkt durch alle SyntaxTrees und prüft jedes Symbol gegen die Allowlist.

```csharp
// Pseudo-Code

foreach (var tree in syntaxTrees) {
    var model = compilation.GetSemanticModel(tree);
    var walker = new ValidationWalker(model, allowlist);
    walker.Visit(tree.GetRoot());
    
    foreach (var violation in walker.Violations) {
        diagnostics.Add(new Diagnostic(
            Severity.Error,
            Code: violation.RuleId,  // e.g. "RZS1001"
            Message: violation.Message,
            Location: violation.Location  // File + Line
        ));
    }
}

if (diagnostics.Any(d => d.Severity == Severity.Error)) {
    PrintDiagnostics(diagnostics);
    return ExitCode.ValidationFailed;
}
```

Bei Fail: klare Error-Messages mit RZS-Code, File, Zeile, Fix-Hinweis.

---

## Transpilation-Pass

Die eigentliche C# → JS Transformation. Läuft zweistufig:

### Stufe 1: User-Code + generated Code (via SyntaxWalker)

```csharp
// Pseudo-Code

public class JsEmitter : CSharpSyntaxWalker {
    private readonly SemanticModel _model;
    private readonly StringBuilder _output;
    
    public override void VisitClassDeclaration(ClassDeclarationSyntax node) {
        _output.Append($"export class {node.Identifier}");
        // ... emit extends, generics, body
        base.VisitClassDeclaration(node);
    }
    
    public override void VisitMethodDeclaration(MethodDeclarationSyntax node) {
        // Mapping: C# method → JS method
        // Await-Handling, Generics-Erasure, etc.
    }
    
    public override void VisitBinaryExpression(BinaryExpressionSyntax node) {
        var symbol = _model.GetSymbolInfo(node).Symbol;
        
        // User-defined Operator?
        if (symbol is IMethodSymbol method && method.IsUserDefinedOperator()) {
            EmitMethodCall(method, node.Left, node.Right);
            return;
        }
        
        // Standard binary
        base.VisitBinaryExpression(node);
    }
    
    // ... 50+ Visit-Methoden für alle C#-Konstrukte
}
```

### Stufe 2: RenderTreeBuilder-Code (Spezial-Walker)

Komponentens `BuildRenderTree(RenderTreeBuilder)` wird nicht mit dem normalen Walker transpiliert. Ein dedizierter Walker erkennt die Muster und emittiert Hyperscript:

```csharp
// Pseudo-Code

public class RenderTreeWalker : CSharpSyntaxWalker {
    // __builder.OpenElement(seq, "div") → h("div", props, children
    // __builder.AddAttribute(seq, "class", v) → props["class"] = v
    // __builder.AddContent(seq, x) → children.push(x)
    // __builder.CloseElement() → emit h()-call
    
    // Der Walker führt einen Stack mit, baut eine h()-Call-Struktur auf
}
```

---

## Runtime-Injection

Nach Transpilation brauchen die JS-Files Imports auf die Razorshave-Runtime:

```js
// Am Anfang jedes transpilierten File:
import { Component, h } from '@razorshave/runtime';
import { MouseEventArgs } from '@razorshave/runtime/events';
// ... je nach verwendeten Features
```

Der Transpiler sammelt welche Runtime-Features er emittiert hat und fügt die Imports passend ein. Import-Elimination macht esbuild beim Tree-Shaking.

---

## Bundling via esbuild

Nach Transpilation haben wir einen Ordner mit JS-Files. esbuild wird aufgerufen:

```csharp
// Pseudo-Code

var esbuildArgs = new[] {
    "--bundle",
    $"--outfile={outputPath}/app.js",
    "--format=esm",
    "--minify",
    "--tree-shaking=true",
    "--sourcemap=no",  // Entscheidung: keine Source-Maps in v0
    "--entry-names=[name].[hash]",
    entryFile
};

RunEmbeddedEsbuild(esbuildArgs);
```

esbuild-Binary ist als embedded Resource im Razorshave.Cli-NuGet-Package, wird beim ersten Lauf ins Temp-Dir extrahiert.

---

## Output-Schreiben

Letzter Schritt:

1. `dist/` Ordner leeren (oder anlegen)
2. `app.[hash].js` + Source-Map (in v0.2+) schreiben
3. `app.[hash].css` aus Scoped-CSS-Bundle kopieren mit Hash
4. `index.html` generieren mit korrekten Hash-Referenzen
5. `wwwroot/` → `dist/` rekursiv kopieren

---

## Error-Reporting

User sieht einheitlichen Output egal wo der Fehler auftritt:

```
MSBuild: compile errors
→ normale .NET-Compiler-Messages
→ Build failed, Razorshave läuft nicht

MSBuild: build succeeded
→ Razorshave: Analyzing...
→ Razorshave: Transpiling...  
→ Razorshave ERROR RZS1001: Symbol 'Microsoft.EntityFrameworkCore.DbContext' 
  not in Razorshave ecosystem
  at Pages/UserList.razor:5
→ Build failed, dist/ not written
```

Klare Phasen-Trennung. User weiß immer welches Tool gemeckert hat.

---

## Performance-Rahmen

Realistische Zeiten pro Build-Durchlauf für mittelgroße App (30 Components):

- MSBuild Build: 5-15 Sekunden (abhängig von Cache)
- Source-Sammlung: <1 Sekunde
- Roslyn-Compilation-Aufbau: 1-3 Sekunden
- Validation-Pass: 1-2 Sekunden
- Transpilation-Pass: 2-5 Sekunden
- esbuild-Bundling: 0.5-1 Sekunde
- Output-Writing: <1 Sekunde

**Gesamt: ~10-30 Sekunden.** Für Production-Builds akzeptabel. Kein Watch-Mode, keine Incremental-Builds in v0.

---

---

## Entschieden

- **Integration-Modus:** MSBuild-Target (nicht Standalone-CLI). User-Workflow bleibt `dotnet build -c Razorshave`.
- **Compilation-Artefakt:** Source-basiert (Roslyn SyntaxTrees), nicht IL-basiert.
- **DLL-Transpilation:** niemals. Nur Source-Code wird transpiliert. Externe NuGet-Packages ohne Source sind nicht transpilierbar.
- **Assembly-References:** kommen von MSBuild via `ReferencePath`-TaskItems. MSBuild hat alles bereits resolved, verifiziert und pfad-konkret bereitgestellt.
- **MSBuild-Properties:** via TaskItem-Injection oder MSBuild-API (`Microsoft.Build.Evaluation`). Kein eigenes Parsen von `project.assets.json` oder `.csproj`.
- **Source-Generator-Output-Pfad:** via MSBuild-Property `$(CompilerGeneratedFilesOutputPath)`, nicht hardcodet.
- **Target-Configuration:** neuer Build-Configuration "Razorshave" — `dotnet build -c Razorshave` triggert Razorshave-Target automatisch.

## Noch zu klären

- **Parallelization:** Validation + Transpilation pro File parallel? Nice-to-have, entscheidbar wenn Performance-Messungen vorliegen. v0.2-Thema.

