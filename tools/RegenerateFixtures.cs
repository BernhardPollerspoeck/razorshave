using System.Diagnostics;

// Razorshave fixture regenerator — file-based app (`dotnet run tools/RegenerateFixtures.cs`).
//
// For each target component in the fixture list:
//   1. Builds KitchenSink.Client with EmitCompilerGeneratedFiles=true so the
//      Razor source generator writes .razor.g.cs to obj/.
//   2. Copies the original .razor and the generated .razor.g.cs into
//      tests/Razorshave.Transpiler.Tests/Fixtures/<name>/
//
// Why this exists: Microsoft may change Razor's emission between SDK patches.
// Committing the generated files as test fixtures keeps the transpiler's
// snapshot tests deterministic. When the SDK moves, re-run this script and
// review what changed.
//
// Run from repo root:
//   dotnet run tools/RegenerateFixtures.cs

var repoRoot = Directory.GetCurrentDirectory();
var kitchenSink = Path.Combine(repoRoot, "e2e", "KitchenSink.Client");
var csproj = Path.Combine(kitchenSink, "KitchenSink.Client.csproj");
var fixturesRoot = Path.Combine(repoRoot, "tests", "Razorshave.Transpiler.Tests", "Fixtures");
var generatedRoot = Path.Combine(kitchenSink, "obj", "Debug", "net10.0", "generated",
    "Microsoft.CodeAnalysis.Razor.Compiler",
    "Microsoft.NET.Sdk.Razor.SourceGenerators.RazorSourceGenerator");

// Fixture targets. Add a tuple here to pin a new reference component.
// RazorPath / GeneratedPath are slash-separated, relative to KitchenSink root / generatedRoot.
var targets = new (string Name, string RazorPath, string GeneratedPath)[]
{
    ("counter",    "Components/Pages/Counter.razor",     "Components/Pages/Counter_razor.g.cs"),
    ("weather",    "Components/Pages/Weather.razor",     "Components/Pages/Weather_razor.g.cs"),
    ("mainlayout", "Components/Layout/MainLayout.razor", "Components/Layout/MainLayout_razor.g.cs"),
};

// --- Step 1: build KitchenSink ---
Console.WriteLine("Building KitchenSink.Client (EmitCompilerGeneratedFiles=true) ...");
var psi = new ProcessStartInfo("dotnet")
{
    UseShellExecute = false,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    WorkingDirectory = repoRoot,
};
psi.ArgumentList.Add("build");
psi.ArgumentList.Add(csproj);
psi.ArgumentList.Add("-p:EmitCompilerGeneratedFiles=true");
psi.ArgumentList.Add("-v:quiet");
psi.ArgumentList.Add("--nologo");

var proc = Process.Start(psi)!;
var stdout = proc.StandardOutput.ReadToEnd();
var stderr = proc.StandardError.ReadToEnd();
proc.WaitForExit();
if (proc.ExitCode != 0)
{
    Console.Error.WriteLine("Build failed. Output:");
    if (stdout.Length > 0) Console.Error.WriteLine(stdout);
    if (stderr.Length > 0) Console.Error.WriteLine(stderr);
    return proc.ExitCode;
}

// --- Step 2: copy each fixture ---
Console.WriteLine();
Console.WriteLine("Copying fixtures:");
Directory.CreateDirectory(fixturesRoot);

foreach (var (name, razorPath, generatedPath) in targets)
{
    var srcRazor = Path.Combine(kitchenSink, razorPath.Replace('/', Path.DirectorySeparatorChar));
    var srcGen = Path.Combine(generatedRoot, generatedPath.Replace('/', Path.DirectorySeparatorChar));

    if (!File.Exists(srcRazor))
    {
        Console.Error.WriteLine($"  missing razor: {srcRazor}");
        return 1;
    }
    if (!File.Exists(srcGen))
    {
        Console.Error.WriteLine($"  missing generated: {srcGen}");
        return 1;
    }

    var dest = Path.Combine(fixturesRoot, name);
    Directory.CreateDirectory(dest);
    var destRazor = Path.Combine(dest, "Input.razor");
    var destGen = Path.Combine(dest, "Input.g.cs");
    File.Copy(srcRazor, destRazor, overwrite: true);
    File.Copy(srcGen, destGen, overwrite: true);

    Console.WriteLine($"  {name,-12}  ← {razorPath}  +  ...{generatedPath}");
}

Console.WriteLine();
Console.WriteLine($"{targets.Length} fixtures regenerated under tests/Razorshave.Transpiler.Tests/Fixtures/");
Console.WriteLine();
Console.WriteLine("Review diffs with `git diff tests/Razorshave.Transpiler.Tests/Fixtures/` before committing.");
return 0;
