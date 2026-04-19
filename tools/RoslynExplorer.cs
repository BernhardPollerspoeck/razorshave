#:package Microsoft.CodeAnalysis.CSharp

using System.Runtime.InteropServices;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Razorshave Roslyn exploration tool — file-based app (`dotnet run tools/RoslynExplorer.cs`).
//
// Reads a Razor-generated .razor.g.cs file, parses it, builds a compilation with
// the KitchenSink references and dumps what Roslyn gives us:
//   - class-level info (modifiers, base, attributes, members)
//   - every __builder.* invocation in BuildRenderTree, with its resolved method
//     symbol (fully qualified, generic type-args exposed)
//   - for each AddContent/AddAttribute with a non-constant argument, the symbol
//     and type of that expression (fields, methods, generic factory calls)
//
// Used to drive design decisions for the transpiler walkers (RAZORSHAVE-BOOTSTRAP.md 5.1).
//
// Run from repo root. Build KitchenSink.Client first so .g.cs files and bin refs exist:
//   dotnet build e2e/KitchenSink.Client/KitchenSink.Client.csproj
//   dotnet run tools/RoslynExplorer.cs                           # default: Counter
//   dotnet run tools/RoslynExplorer.cs -- path/to/Other.g.cs     # pass any .g.cs

var repoRoot = Directory.GetCurrentDirectory();
var binDir = Path.Combine(repoRoot, "e2e", "KitchenSink.Client", "bin", "Debug", "net10.0");
var defaultPath = Path.Combine(repoRoot, "e2e", "KitchenSink.Client", "obj", "Debug", "net10.0",
    "generated", "Microsoft.CodeAnalysis.Razor.Compiler",
    "Microsoft.NET.Sdk.Razor.SourceGenerators.RazorSourceGenerator",
    "Components", "Pages", "Counter_razor.g.cs");

var path = args.Length > 0 ? args[0] : defaultPath;

if (!File.Exists(path))
{
    Console.Error.WriteLine($"File not found: {path}");
    return 1;
}

var source = File.ReadAllText(path);
var tree = CSharpSyntaxTree.ParseText(source, path: path);
var root = (CompilationUnitSyntax)tree.GetRoot();

// --- Collect references: Shared Framework (Microsoft.NETCore.App + AspNetCore.App) + KitchenSink bin ---
var refs = new List<MetadataReference>();
var refPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

void TryAdd(string dll)
{
    if (refPaths.Add(dll))
    {
        try { refs.Add(MetadataReference.CreateFromFile(dll)); }
        catch { /* unreadable */ }
    }
}

// NETCore.App is the directory where System.Private.CoreLib.dll lives. GetRuntimeDirectory
// returns a path with trailing separator — trim it or directory math goes off by one level.
var netcoreDir = Path.TrimEndingDirectorySeparator(RuntimeEnvironment.GetRuntimeDirectory());
foreach (var dll in Directory.GetFiles(netcoreDir, "*.dll"))
    TryAdd(dll);

// Find AspNetCore.App next to NETCore.App (same shared-framework root, sibling folder)
var sharedRoot = Path.GetDirectoryName(Path.GetDirectoryName(netcoreDir));
if (sharedRoot is not null)
{
    var aspnetRoot = Path.Combine(sharedRoot, "Microsoft.AspNetCore.App");
    if (Directory.Exists(aspnetRoot))
    {
        var netcoreMajor = ParseVersion(new DirectoryInfo(netcoreDir).Name)?.Major ?? 0;
        var match = Directory.GetDirectories(aspnetRoot)
            .Select(d => (Path: d, Version: ParseVersion(Path.GetFileName(d))))
            .Where(x => x.Version is not null && x.Version.Major == netcoreMajor)
            .OrderByDescending(x => x.Version!)
            .FirstOrDefault();
        if (match.Path is not null)
            foreach (var dll in Directory.GetFiles(match.Path, "*.dll"))
                TryAdd(dll);
    }
}

// KitchenSink itself (for the Counter type etc.)
if (Directory.Exists(binDir))
    foreach (var dll in Directory.GetFiles(binDir, "*.dll"))
        TryAdd(dll);
var compilation = CSharpCompilation.Create(
    "Explorer",
    [tree],
    refs,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
var model = compilation.GetSemanticModel(tree);

Console.WriteLine("================================================================");
Console.WriteLine($"  File: {Path.GetFileName(path)}");
Console.WriteLine($"  Size: {source.Length:N0} chars, {source.Split('\n').Length:N0} lines");
Console.WriteLine($"  Root: {root.Kind()}   Descendants: {root.DescendantNodes().Count():N0}");
Console.WriteLine($"  Refs: {refs.Count} assemblies from {Path.GetFileName(binDir)}/");
Console.WriteLine("================================================================");

// --- Classes ---
Console.WriteLine();
Console.WriteLine("### Classes");
foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
{
    Console.WriteLine();
    Console.WriteLine($"  class {cls.Identifier.Text}   (modifiers: {string.Join(" ", cls.Modifiers)})");
    if (cls.BaseList is not null)
        foreach (var bt in cls.BaseList.Types)
            Console.WriteLine($"    : {bt}");
    foreach (var attr in cls.AttributeLists.SelectMany(a => a.Attributes))
    {
        var attrText = attr.ToString().Replace("\r\n", " ").Replace("\n", " ");
        if (attrText.Length > 120) attrText = attrText[..120] + "…";
        Console.WriteLine($"    [attr] {attrText}");
    }
    foreach (var member in cls.Members)
    {
        var label = member switch
        {
            MethodDeclarationSyntax m => $"method   {m.Modifiers} {m.ReturnType} {m.Identifier}({m.ParameterList.Parameters.Count} params)",
            FieldDeclarationSyntax f => $"field    {f.Modifiers} {f.Declaration}",
            PropertyDeclarationSyntax p => $"property {p.Modifiers} {p.Type} {p.Identifier}",
            ClassDeclarationSyntax nc => $"nested   class {nc.Identifier}",
            _ => $"{member.Kind()}"
        };
        label = label.Replace("\r\n", " ").Replace("\n", " ");
        if (label.Length > 150) label = label[..150] + "…";
        Console.WriteLine($"    - {label}");
    }
}

static Version? ParseVersion(string s)
{
    var head = s.Split('-')[0];
    return Version.TryParse(head, out var v) ? v : null;
}

// Helper: recursively find all render-tree invocations, whether on __builder, __builder2, __builder3, etc.
static IEnumerable<InvocationExpressionSyntax> BuilderCalls(SyntaxNode scope)
    => scope.DescendantNodes().OfType<InvocationExpressionSyntax>()
        .Where(inv => inv.Expression is MemberAccessExpressionSyntax mae
                      && mae.Expression is IdentifierNameSyntax id
                      && id.Identifier.Text.StartsWith("__builder", StringComparison.Ordinal));

var buildMethod = root.DescendantNodes()
    .OfType<MethodDeclarationSyntax>()
    .FirstOrDefault(m => m.Identifier.Text == "BuildRenderTree");

if (buildMethod is null)
{
    Console.WriteLine();
    Console.WriteLine("### (no BuildRenderTree method found)");
    return 0;
}

// --- __builder.* invocations with semantic info ---
Console.WriteLine();
Console.WriteLine("### __builder.* invocations (name, generic-args, resolved symbol)");

var invocations = BuilderCalls(buildMethod).ToList();
var idx = 0;
foreach (var inv in invocations)
{
    var mae = (MemberAccessExpressionSyntax)inv.Expression;
    var receiver = ((IdentifierNameSyntax)mae.Expression).Identifier.Text;
    var methodName = mae.Name is GenericNameSyntax g
        ? g.Identifier.Text + "<" + string.Join(", ", g.TypeArgumentList.Arguments.Select(a => a.ToString())) + ">"
        : mae.Name.Identifier.Text;

    var sym = model.GetSymbolInfo(inv).Symbol as IMethodSymbol;
    var symStr = sym is null
        ? "(UNRESOLVED)"
        : $"{sym.ContainingType}.{sym.Name}" + (sym.IsGenericMethod
            ? "<" + string.Join(", ", sym.TypeArguments.Select(t => t.Name)) + ">"
            : "");

    Console.WriteLine($"  [{idx++,2}] {receiver}.{methodName}   →   {symStr}");
}

// --- Interesting arguments: non-constant args to AddAttribute / AddContent ---
// Shows what Roslyn thinks the user-expression resolves to.
Console.WriteLine();
Console.WriteLine("### Arguments that reference user code (field/method/complex expr)");

int shown = 0;
foreach (var inv in invocations)
{
    var mae = (MemberAccessExpressionSyntax)inv.Expression;
    var methodName = mae.Name is GenericNameSyntax g ? g.Identifier.Text : mae.Name.Identifier.Text;
    if (methodName is not ("AddContent" or "AddAttribute")) continue;

    for (int i = 0; i < inv.ArgumentList.Arguments.Count; i++)
    {
        var argExpr = inv.ArgumentList.Arguments[i].Expression;
        // Skip trivial constants / strings / numeric literals
        if (argExpr is LiteralExpressionSyntax) continue;
        // Skip the simple "seq" numeric arg (always index 0, LiteralExpression already skipped)

        var argSym = model.GetSymbolInfo(argExpr).Symbol;
        var argType = model.GetTypeInfo(argExpr).Type;

        if (argSym is null && argType is null) continue;

        var argText = argExpr.ToString().Replace("\r\n", " ").Replace("\n", " ");
        if (argText.Length > 80) argText = argText[..80] + "…";

        var symKind = argSym switch
        {
            IFieldSymbol f => $"field   {f.Type} {f.Name}",
            IMethodSymbol m => $"method  {m.ReturnType} {m.ContainingType.Name}.{m.Name}"
                                + (m.IsGenericMethod
                                    ? "<" + string.Join(", ", m.TypeArguments.Select(t => t.Name)) + ">"
                                    : ""),
            IPropertySymbol p => $"prop    {p.Type} {p.Name}",
            ILocalSymbol l => $"local   {l.Type} {l.Name}",
            IParameterSymbol ps => $"param   {ps.Type} {ps.Name}",
            null => "(complex)",
            _ => argSym.Kind.ToString()
        };
        var typeStr = argType?.ToDisplayString() ?? "?";

        Console.WriteLine($"  {methodName}[{i}]: {argText}");
        Console.WriteLine($"    symbol: {symKind}");
        Console.WriteLine($"    type:   {typeStr}");
        shown++;
    }
}
if (shown == 0) Console.WriteLine("  (none — render tree only uses literals)");

// --- Diagnostics: any compilation errors we should know about? ---
var diagnostics = compilation.GetDiagnostics()
    .Where(d => d.Severity == DiagnosticSeverity.Error
             && d.Location.SourceTree == tree)  // only this file, skip metadata-ref issues
    .Take(5)
    .ToList();
if (diagnostics.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("### Compilation diagnostics (errors in this file only, first 5)");
    foreach (var d in diagnostics)
        Console.WriteLine($"  {d.Severity}: {d.Id} — {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)} @ {d.Location.GetLineSpan().StartLinePosition}");
}

return 0;
