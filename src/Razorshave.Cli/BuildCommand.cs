using System.Diagnostics;
using System.Globalization;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using Razorshave.Cli.Transpiler;

using static Razorshave.Cli.Transpiler.Transpiler;

namespace Razorshave.Cli;

/// <summary>
/// Implements <c>razorshave build &lt;project&gt;</c>: runs the underlying
/// <c>dotnet build</c> so the Razor source generator emits its <c>.razor.g.cs</c>
/// files, transpiles each Razor component class to its JavaScript module,
/// extracts routing metadata, copies the runtime, and writes an
/// <c>index.html</c> + generated <c>main.js</c> into <c>&lt;project&gt;/dist/</c>.
/// </summary>
/// <remarks>
/// <para>
/// Phase-1 + Phase-2 implementation of RAZORSHAVE-BOOTSTRAP.md step 5.13 —
/// unbundled ESM, Router-wired main.js, DefaultLayout wrap from Routes.razor.
/// Phases 3+ layer esbuild bundling and MSBuild-task integration on top.
/// </para>
/// <para>
/// Routes.razor is special-cased: it is not transpiled as a regular component,
/// its <c>.razor.g.cs</c> is mined once for the <c>DefaultLayout</c> and
/// <c>NotFoundPage</c> bindings Blazor's <c>Router</c> would consume. Those
/// values become props on the Razorshave-runtime Router we instantiate in
/// the generated main.js.
/// </para>
/// </remarks>
internal static class BuildCommand
{
    // Exit codes — documented so callers (MSBuild task, CI scripts) can
    // switch on them meaningfully. Non-zero always means "don't ship this
    // dist/".
    private const int ExitOk = 0;
    private const int ExitProjectDirMissing = 2;
    private const int ExitCsprojMissing = 3;
    private const int ExitGeneratedRoot = 4;
    private const int ExitNoComponents = 5;
    private const int ExitRuntimeSrcMissing = 6;
    private const int ExitEsbuildBinaryMissing = 7;
    private const int ExitEsbuildMetaMissing = 8;
    private const int ExitTargetFrameworkMissing = 9;

    /// <summary>
    /// Entry point. <paramref name="skipDotnetBuild"/> is set by the MSBuild
    /// task path — MSBuild already built the project once, a nested build
    /// would recurse. CLI direct usage leaves it false so the tool is
    /// one-shot for developers.
    /// </summary>
    public static int Run(string projectPath, bool skipDotnetBuild = false, string configuration = "Debug")
    {
        var absProject = Path.GetFullPath(projectPath);
        if (!Directory.Exists(absProject))
        {
            Console.Error.WriteLine($"razorshave: project directory not found: {absProject}");
            return ExitProjectDirMissing;
        }

        var csproj = FindCsproj(absProject);
        if (csproj is null)
        {
            Console.Error.WriteLine($"razorshave: no .csproj found in {absProject}");
            return ExitCsprojMissing;
        }

        var tfm = ReadTargetFramework(csproj);
        if (tfm is null)
        {
            Console.Error.WriteLine($"razorshave: could not read <TargetFramework> from {Path.GetFileName(csproj)}");
            return ExitTargetFrameworkMissing;
        }

        if (!skipDotnetBuild)
        {
            Console.WriteLine($"[1/6] Building {Path.GetFileName(csproj)} ...");
            var buildExit = RunDotnetBuild(csproj);
            if (buildExit != 0) return buildExit;
        }
        else
        {
            Console.WriteLine($"[1/6] Skipping dotnet build (MSBuild-task context, -c {configuration})");
        }

        var generatedRoot = FindGeneratedRazorRoot(absProject, configuration, tfm);
        if (generatedRoot is null)
        {
            Console.Error.WriteLine($"razorshave: expected Razor-generated sources under obj/{configuration}/ — did EmitCompilerGeneratedFiles fail?");
            return ExitGeneratedRoot;
        }

        // Staging pattern — we write everything into `dist-staging/` first,
        // then atomic-rename into `dist/` on success. A failed transpile no
        // longer leaves the user with an empty dist directory and no
        // deployable artefact from the previous successful build.
        var finalDistDir = Path.Combine(absProject, "dist");
        var distDir = Path.Combine(absProject, "dist-staging");
        if (Directory.Exists(distDir)) Directory.Delete(distDir, recursive: true);
        Directory.CreateDirectory(distDir);

        Console.WriteLine("[2/6] Transpiling components ...");
        // Build the full reference list once: shared framework + the user
        // project's output DLLs. Every transpile call in this invocation
        // shares the same list, avoiding the per-Transpile concat hot loop
        // that previously ran for each component.
        var references = BuildReferenceList(absProject, configuration, tfm);
        // Pick up the project's GlobalUsings.g.cs so implicit-using types
        // (e.g. `HttpClient` from Web SDK's `System.Net.Http`) resolve in
        // SemanticModel just as they do in the real build.
        var globalUsings = LoadGlobalUsings(absProject, configuration, tfm);
        var (components, routesConfig) = TranspileAll(generatedRoot, distDir, references, globalUsings);
        if (components.Count == 0)
        {
            Console.Error.WriteLine("razorshave: no component classes found");
            return ExitNoComponents;
        }

        var clients = TranspileClientClasses(absProject, distDir, references, globalUsings);

        Console.WriteLine("[3/6] Copying runtime ...");
        var runtimeSrc = FindRuntimeSource();
        if (runtimeSrc is null)
        {
            Console.Error.WriteLine("razorshave: runtime source directory not found");
            return ExitRuntimeSrcMissing;
        }
        var runtimeStaging = Path.Combine(distDir, "runtime");
        CopyDirectory(runtimeSrc, runtimeStaging);

        Console.WriteLine("[4/6] Writing main.js entry ...");
        WriteAppJs(distDir, absProject, components, routesConfig, clients);

        Console.WriteLine("[5/6] Bundling with esbuild ...");
        var esbuild = FindEsbuildBinary();
        if (esbuild is null)
        {
            Console.Error.WriteLine("razorshave: esbuild not found in Razorshave.Runtime/node_modules/.bin/ — run `npm install` in the runtime project.");
            return ExitEsbuildBinaryMissing;
        }
        var bundleResult = RunEsbuild(esbuild, distDir, runtimeStaging);
        if (bundleResult.Exit != 0) return bundleResult.Exit;

        Console.WriteLine("[6/6] Finalising dist/ (prune unbundled sources, copy wwwroot + scoped CSS) ...");
        FinaliseDist(distDir, absProject, runtimeStaging, bundleResult.OutputFileName, configuration, tfm);

        // Atomic promotion of staging → dist. If anything blew up above, the
        // previous `dist/` is still intact and the build can be retried.
        if (Directory.Exists(finalDistDir)) Directory.Delete(finalDistDir, recursive: true);
        Directory.Move(distDir, finalDistDir);
        distDir = finalDistDir;

        Console.WriteLine();
        var routedCount = components.Count(c => c.RoutePatterns.Count > 0);
        Console.WriteLine($"✓ razorshave: {components.Count} component(s), {routedCount} routed, {clients.Count} client service(s) → {distDir}");
        foreach (var c in components)
        {
            var marker = c.RoutePatterns.Count > 0 ? $"  [{string.Join(", ", c.RoutePatterns)}]" : "";
            Console.WriteLine($"    {c.Name}.js{marker}");
        }
        foreach (var c in clients)
        {
            var marker = c.InterfaceKeys.Count > 0 ? $"  →{{ {string.Join(", ", c.InterfaceKeys)} }}" : "";
            Console.WriteLine($"    {c.Name}.js (client){marker}");
        }
        if (routesConfig.DefaultLayout is not null)
            Console.WriteLine($"    DefaultLayout: {routesConfig.DefaultLayout}");
        if (routesConfig.NotFound is not null)
            Console.WriteLine($"    NotFound: {routesConfig.NotFound}");
        return ExitOk;
    }

    // Walks every user .cs file in the project tree, looking for classes
    // marked `[Client]`. Each match gets transpiled to `dist/<Name>.js` and
    // collects its implemented interface names for auto-registration in the
    // generated main.js. Folders that are build artefacts (bin/, obj/) are
    // skipped so we don't pick up IDE-generated junk.
    private static List<TranspiledClient> TranspileClientClasses(string projectDir, string distDir, IReadOnlyList<MetadataReference> references, string? globalUsings)
    {
        var clients = new List<TranspiledClient>();

        foreach (var csFile in EnumerateUserCsFiles(projectDir))
        {
            string source;
            try { source = File.ReadAllText(csFile); }
            catch { continue; }

            if (!source.Contains("[Client]", StringComparison.Ordinal)) continue;

            // Parse once, reuse for both classification and emission — the
            // old path parsed the same file a second time inside
            // TranspileClientClass, doubling the work per [Client] class.
            var tree = CSharpSyntaxTree.ParseText(source);
            var matches = tree.GetRoot().DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(Razorshave.Cli.Transpiler.ComponentClassifier.IsClientClass)
                .ToList();
            if (matches.Count == 0) continue;

            // Loop per matching class — a file that declares more than one
            // [Client] used to silently drop the extras (FirstOrDefault), so
            // two services in the same file meant the second one never reached
            // the bundle. Each class gets its own dist/<Name>.js and its own
            // interface-key list so DI-auto-registration sees all of them.
            foreach (var cls in matches)
            {
                var js = Razorshave.Cli.Transpiler.Transpiler.TranspileClientClass(tree, cls, references, globalUsings);
                if (string.IsNullOrWhiteSpace(js)) continue;

                var name = cls.Identifier.Text;
                var outFile = Path.Combine(distDir, $"{name}.js");
                File.WriteAllText(outFile, js);

                var interfaces = Razorshave.Cli.Transpiler.ComponentClassifier.EnumerateInterfaces(cls).ToArray();
                clients.Add(new TranspiledClient(name, outFile, interfaces));
            }
        }
        return clients;
    }

    private static IEnumerable<string> EnumerateUserCsFiles(string projectDir)
    {
        foreach (var file in Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(projectDir, file).Replace('\\', '/');
            if (rel.StartsWith("bin/", StringComparison.Ordinal)) continue;
            if (rel.StartsWith("obj/", StringComparison.Ordinal)) continue;
            yield return file;
        }
    }

    // --- Build ---

    private static int RunDotnetBuild(string csproj)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("build");
        psi.ArgumentList.Add(csproj);
        psi.ArgumentList.Add("-p:EmitCompilerGeneratedFiles=true");
        psi.ArgumentList.Add("-v:quiet");
        psi.ArgumentList.Add("--nologo");

        var proc = Process.Start(psi)!;
        // Read stdout and stderr concurrently — sequentially draining one
        // then the other can deadlock when the child writes enough to the
        // un-drained pipe to fill its buffer.
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        Task.WaitAll(stdoutTask, stderrTask);
        var stdout = stdoutTask.Result;
        var stderr = stderrTask.Result;
        proc.WaitForExit();
        if (proc.ExitCode != 0)
        {
            Console.Error.WriteLine("razorshave: underlying dotnet build failed:");
            if (stdout.Length > 0) Console.Error.WriteLine(stdout);
            if (stderr.Length > 0) Console.Error.WriteLine(stderr);
        }
        return proc.ExitCode;
    }

    // --- Transpile ---

    private sealed record TranspiledComponent(
        string Name,
        string OutputFile,
        IReadOnlyList<string> RoutePatterns);

    private sealed record TranspiledClient(
        string Name,
        string OutputFile,
        IReadOnlyList<string> InterfaceKeys);

    private static (List<TranspiledComponent> components, RouteExtractor.RoutesConfig routesConfig)
        TranspileAll(string generatedRoot, string distDir, IReadOnlyList<MetadataReference> references, string? globalUsings)
    {
        var components = new List<TranspiledComponent>();
        var routesConfig = RouteExtractor.RoutesConfig.Empty;

        foreach (var genFile in Directory.EnumerateFiles(generatedRoot, "*_razor.g.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(genFile);
            var tree = CSharpSyntaxTree.ParseText(source);
            var componentClasses = tree.GetRoot().DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(IsComponentSubclass)
                .ToList();
            if (componentClasses.Count == 0) continue;

            // Loop per component class — a single file with more than one
            // Component (rare, but legal via code-behind) would otherwise lose
            // everything past the first. Each class gets its own dist/<Name>.js
            // and its own route entry so the router picks every page up.
            foreach (var componentCls in componentClasses)
            {
                var className = componentCls.Identifier.Text;

                // Routes.razor is not transpiled — its role (hosting Blazor's
                // Router) is replaced by the runtime Router we wire up in main.js.
                // We do peek at it for DefaultLayout / NotFoundPage metadata.
                if (className == "Routes")
                {
                    routesConfig = RouteExtractor.ExtractRoutesConfig(tree);
                    continue;
                }

                var js = Transpile(tree, componentCls, references, globalUsings);
                if (string.IsNullOrWhiteSpace(js)) continue;

                var outFile = Path.Combine(distDir, $"{className}.js");
                File.WriteAllText(outFile, js);

                var patterns = RouteExtractor.ExtractRoutePatterns(componentCls);
                components.Add(new TranspiledComponent(className, outFile, patterns));
            }
        }

        return (
            components.OrderBy(c => c.Name, StringComparer.Ordinal).ToList(),
            routesConfig);
    }

    // Build the full metadata-reference list for one BuildCommand.Run. Shared
    // framework DLLs come from the static loader (cached because they don't
    // change during the process lifetime); project-local DLLs are loaded
    // fresh every time because the user may rebuild mid-session.
    //
    // Centralising the merge here avoids re-doing the concat on every
    // Transpile call and means SemanticModel-dependent detections (event
    // symbols, [JsonPropertyName], inherited-member rewrites) consistently
    // see the same reference set across the whole build.
    private static List<MetadataReference> BuildReferenceList(string projectDir, string configuration, string tfm)
    {
        var refs = new List<MetadataReference>(MetadataReferenceLoader.SharedFramework());
        refs.AddRange(LoadProjectBinReferences(projectDir, configuration, tfm));
        return refs;
    }

    // Reads `<Project>.GlobalUsings.g.cs` from the user's obj dir. MSBuild
    // generates this file for Web and other SDKs that ship implicit-using
    // declarations (`global using System.Net.Http;` for Web, for example).
    // Returns null when the file doesn't exist — projects without Web SDK
    // don't get the file and work fine without these globals.
    private static string? LoadGlobalUsings(string projectDir, string configuration, string tfm)
    {
        var objDir = Path.Combine(projectDir, "obj", configuration, tfm);
        if (!Directory.Exists(objDir)) return null;
        var file = Directory.EnumerateFiles(objDir, "*.GlobalUsings.g.cs", SearchOption.TopDirectoryOnly).FirstOrDefault();
        return file is not null ? File.ReadAllText(file) : null;
    }

    // Parses the <TargetFramework> element from the project's csproj. Falls
    // back to `null` if the project uses multi-targeting (<TargetFrameworks>)
    // or the element is missing — both cases require explicit handling the
    // caller should surface as an error.
    private static string? ReadTargetFramework(string csprojPath)
    {
        try
        {
            var doc = System.Xml.Linq.XDocument.Load(csprojPath);
            // Csproj files use no XML namespace for SDK-style projects, so
            // plain LocalName matching works without xmlns gymnastics.
            return doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "TargetFramework")
                ?.Value?.Trim();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"razorshave: failed to read TargetFramework from {csprojPath}: {ex.Message}");
            return null;
        }
    }

    private static List<MetadataReference> LoadProjectBinReferences(string projectDir, string configuration, string tfm)
    {
        // Scan the user project's output bin for DLLs other than the SDK-shared
        // ones (those already live in MetadataReferenceLoader.SharedFramework()).
        // This is a pragmatic stand-in for reading @(ReferencePath) from MSBuild;
        // revisit if the signal gets noisy.
        var binDir = Path.Combine(projectDir, "bin", configuration, tfm);
        if (!Directory.Exists(binDir)) return [];

        // Skip the user project's own output DLL. Including it loads a
        // compiled copy of the same types we're parsing from source —
        // SemanticModel then sees duplicate definitions, bails out on
        // resolution, and every `[JsonPropertyName]` / event-symbol /
        // inherited-member lookup silently fails.
        var csproj = Directory.EnumerateFiles(projectDir, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
        var selfDllName = csproj is null
            ? null
            : Path.GetFileNameWithoutExtension(csproj) + ".dll";

        var refs = new List<MetadataReference>();
        foreach (var dll in Directory.GetFiles(binDir, "*.dll", SearchOption.TopDirectoryOnly))
        {
            if (selfDllName is not null
                && string.Equals(Path.GetFileName(dll), selfDllName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            try
            {
                refs.Add(MetadataReference.CreateFromFile(dll));
            }
            catch (Exception ex)
            {
                // Same rationale as MetadataReferenceLoader.TryAdd: silently
                // dropped references cause silently wrong JS output. Log it.
                Console.Error.WriteLine($"razorshave: skipping unreadable project reference {dll}: {ex.Message}");
            }
        }
        return refs;
    }

    private static ClassDeclarationSyntax? FindComponentClass(SyntaxTree tree)
    {
        return tree.GetRoot().DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(IsComponentSubclass);
    }

    private static bool IsComponentSubclass(ClassDeclarationSyntax cls)
        => Transpiler.ComponentClassifier.IsRazorComponent(cls);

    // --- Runtime + Assets ---

    private static string? FindRuntimeSource()
    {
        // NuGet-package layout first: the assembly sits in tasks/net10.0/ and
        // the runtime files land in build/runtime/ alongside it. A consumer
        // installing via <PackageReference> hits this path.
        var asmPath = typeof(BuildCommand).Assembly.Location;
        if (!string.IsNullOrEmpty(asmPath))
        {
            var asmDir = Path.GetDirectoryName(asmPath);
            if (asmDir is not null)
            {
                // asmDir = <pkg>/tasks/net10.0/ → ../../build/runtime/
                var packaged = Path.GetFullPath(Path.Combine(asmDir, "..", "..", "build", "runtime"));
                if (Directory.Exists(packaged)) return packaged;
            }
        }

        // Dev-repo fallback: walk up from the assembly location and the CWD
        // looking for the mono-repo checkout. Covers `dotnet run`, tests, and
        // MSBuild-loaded Debug copies during local development.
        var roots = new List<string>();
        if (!string.IsNullOrEmpty(asmPath))
        {
            roots.Add(Path.GetDirectoryName(asmPath)!);
        }
        roots.Add(Environment.CurrentDirectory);

        foreach (var start in roots)
        {
            var dir = new DirectoryInfo(start);
            for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "src", "Razorshave.Runtime", "src");
                if (Directory.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            if (rel.EndsWith(".test.js", StringComparison.Ordinal)) continue;

            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    // --- main.js + index.html ---

    /// <summary>
    /// Look for a <c>razorshave.init.js</c> next to the project's csproj — an
    /// escape hatch for anything the transpiler doesn't emit yet (custom
    /// ApiClient subclasses, extra <c>container.register</c> calls, etc.).
    /// If present, its contents get copied to <c>dist/</c> and imported at
    /// the top of the generated main.js.
    /// </summary>
    private static string? CopyInitScriptIfPresent(string projectDir, string distDir)
    {
        var initScript = Path.Combine(projectDir, "razorshave.init.js");
        if (!File.Exists(initScript)) return null;

        var targetName = "razorshave.init.js";
        File.Copy(initScript, Path.Combine(distDir, targetName), overwrite: true);
        return targetName;
    }

    private static void WriteAppJs(
        string distDir,
        string projectDir,
        IReadOnlyList<TranspiledComponent> components,
        RouteExtractor.RoutesConfig routesConfig,
        IReadOnlyList<TranspiledClient> clients)
    {
        var routable = components.Where(c => c.RoutePatterns.Count > 0).ToList();
        var notFound = routesConfig.NotFound is { } nf
            ? components.FirstOrDefault(c => c.Name == nf)
            : null;
        var layout = routesConfig.DefaultLayout is { } dl
            ? components.FirstOrDefault(c => c.Name == dl)
            : null;

        var initScript = CopyInitScriptIfPresent(projectDir, distDir);

        var sb = new StringBuilder();
        sb.AppendLine("// Generated by Razorshave — do not edit.");
        var runtimeImports = clients.Count > 0
            ? "Component, h, mount, Router, container"
            : "Component, h, mount, Router";
        sb.AppendLine(CultureInfo.InvariantCulture, $"import {{ {runtimeImports} }} from '@razorshave/runtime';");
        // Side-effect import — registrations inside razorshave.init.js need
        // to land in the container before the root component is constructed.
        if (initScript is not null)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"import './{initScript}';");
        }

        var importsSeen = new HashSet<string>(StringComparer.Ordinal);
        void Import(string name)
        {
            if (!importsSeen.Add(name)) return;
            sb.AppendLine(CultureInfo.InvariantCulture, $"import {{ {name} }} from './{name}.js';");
        }
        foreach (var c in routable) Import(c.Name);
        if (notFound is not null) Import(notFound.Name);
        if (layout is not null)   Import(layout.Name);
        foreach (var c in clients) Import(c.Name);

        // Register each `[Client]` class under every interface it implements.
        // Done before `mount(...)` so components resolving injects during
        // construction see the bindings. The null argument is passed through
        // the C# primary constructor (`HttpClient http`) — the JS ApiClient
        // base ignores it; fetch() handles all HTTP.
        if (clients.Count > 0)
        {
            sb.AppendLine();
            foreach (var c in clients)
            {
                foreach (var iface in c.InterfaceKeys)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture,
                        $"container.register('{iface}', () => new {c.Name}(null));");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("const routes = [");
        foreach (var c in routable)
        {
            foreach (var pattern in c.RoutePatterns)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {{ pattern: '{pattern}', component: {c.Name} }},");
            }
        }
        sb.AppendLine("];");
        sb.AppendLine();
        sb.AppendLine("class App extends Component {");
        sb.AppendLine("  render() {");
        sb.AppendLine("    return h(Router, {");
        sb.AppendLine("      routes,");
        if (notFound is not null) sb.AppendLine(CultureInfo.InvariantCulture, $"      notFound: {notFound.Name},");
        if (layout is not null)   sb.AppendLine(CultureInfo.InvariantCulture, $"      defaultLayout: {layout.Name},");
        sb.AppendLine("    });");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("mount(App, document.getElementById('app'));");

        File.WriteAllText(Path.Combine(distDir, "main.js"), sb.ToString());
    }

    private static void WriteIndexHtml(string distDir, string bundleFileName, IReadOnlyList<string> cssLinks)
    {
        // Bundled output — esbuild inlines the runtime. index.html pulls the
        // content-hashed bundle plus whatever CSS the project needs: Bootstrap
        // (if present in wwwroot/lib), the project's app.css, and the Razor
        // scoped-CSS bundle the SDK generates under obj/.
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n");
        sb.Append("<html lang=\"en\">\n");
        sb.Append("<head>\n");
        sb.Append("  <meta charset=\"UTF-8\">\n");
        sb.Append("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n");
        sb.Append("  <title>Razorshave</title>\n");
        foreach (var link in cssLinks)
        {
            sb.Append("  <link rel=\"stylesheet\" href=\"").Append(link).Append("\">\n");
        }
        sb.Append("  <link rel=\"icon\" type=\"image/png\" href=\"favicon.png\">\n");
        sb.Append("</head>\n");
        sb.Append("<body>\n");
        sb.Append("  <div id=\"app\"></div>\n");
        sb.Append("  <script type=\"module\" src=\"./").Append(bundleFileName).Append("\"></script>\n");
        sb.Append("</body>\n");
        sb.Append("</html>\n");
        File.WriteAllText(Path.Combine(distDir, "index.html"), sb.ToString());
    }

    // --- esbuild bundling ---

    private sealed record BundleResult(int Exit, string OutputFileName);

    private static string? FindEsbuildBinary()
    {
        // NuGet-package layout: build/esbuild/<rid>/esbuild[.exe].
        var rid = CurrentEsbuildRid();
        var asmPath = typeof(BuildCommand).Assembly.Location;
        if (rid is not null && !string.IsNullOrEmpty(asmPath))
        {
            var asmDir = Path.GetDirectoryName(asmPath);
            if (asmDir is not null)
            {
                var exeName = OperatingSystem.IsWindows() ? "esbuild.exe" : "esbuild";
                var packaged = Path.GetFullPath(Path.Combine(asmDir, "..", "..", "build", "esbuild", rid, exeName));
                if (File.Exists(packaged))
                {
                    EnsureExecutable(packaged);
                    return packaged;
                }
            }
        }

        // Dev-repo fallback: runtime's local `node_modules/.bin/esbuild`.
        var runtimeSrc = FindRuntimeSource();
        if (runtimeSrc is null) return null;
        var runtimeRoot = Path.GetDirectoryName(runtimeSrc);
        if (runtimeRoot is null) return null;

        var binDir = Path.Combine(runtimeRoot, "node_modules", ".bin");
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "esbuild.cmd", "esbuild.exe", "esbuild" }
            : new[] { "esbuild" };

        foreach (var name in candidates)
        {
            var candidate = Path.Combine(binDir, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    // Maps the current OS + process architecture onto one of the four RIDs
    // we pack esbuild binaries for. Returns null if the combination isn't
    // supported — caller falls back to the dev-repo search path, which will
    // then fail with the existing "esbuild not found" diagnostic.
    private static string? CurrentEsbuildRid()
    {
        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
        if (OperatingSystem.IsWindows() && arch == System.Runtime.InteropServices.Architecture.X64)
            return "win-x64";
        if (OperatingSystem.IsLinux() && arch == System.Runtime.InteropServices.Architecture.X64)
            return "linux-x64";
        if (OperatingSystem.IsMacOS() && arch == System.Runtime.InteropServices.Architecture.X64)
            return "osx-x64";
        if (OperatingSystem.IsMacOS() && arch == System.Runtime.InteropServices.Architecture.Arm64)
            return "osx-arm64";
        return null;
    }

    // NuGet restores files read-only and without preserving POSIX exec bits.
    // On Linux/macOS the esbuild binary needs +x before we can invoke it;
    // Windows doesn't care. Best-effort — swallow if we can't chmod (e.g.
    // read-only mount) and let the esbuild launch fail with its own error.
    private static void EnsureExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            var current = File.GetUnixFileMode(path);
            var wanted = current
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherExecute;
            if (wanted != current) File.SetUnixFileMode(path, wanted);
        }
        catch { /* best-effort */ }
    }

    private static BundleResult RunEsbuild(string esbuildPath, string distDir, string runtimeDir)
    {
        var entry = Path.Combine(distDir, "main.js");
        var metaFile = Path.Combine(distDir, ".esbuild-meta.json");

        var psi = new ProcessStartInfo(esbuildPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = distDir,
        };
        psi.ArgumentList.Add(entry);
        psi.ArgumentList.Add("--bundle");
        psi.ArgumentList.Add("--format=esm");
        psi.ArgumentList.Add("--minify");
        // Preserve original class/function names so `Ctor.name` still reads
        // meaningfully at runtime — needed for the devtools-friendly
        // component markers (`<!-- rs:MyWidget -->`) and for reportable
        // error context (owner.constructor.name in trampoline).
        psi.ArgumentList.Add("--keep-names");
        psi.ArgumentList.Add("--tree-shaking=true");
        psi.ArgumentList.Add($"--outdir={distDir}");
        psi.ArgumentList.Add("--entry-names=[name].[hash]");
        psi.ArgumentList.Add($"--alias:@razorshave/runtime={Path.Combine(runtimeDir, "index.js")}");
        psi.ArgumentList.Add($"--metafile={metaFile}");

        var proc = Process.Start(psi)!;
        // Concurrent reads — see RunDotnetBuild for the deadlock rationale.
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        Task.WaitAll(stdoutTask, stderrTask);
        var stdout = stdoutTask.Result;
        var stderr = stderrTask.Result;
        proc.WaitForExit();
        if (proc.ExitCode != 0)
        {
            Console.Error.WriteLine("razorshave: esbuild failed:");
            if (stdout.Length > 0) Console.Error.WriteLine(stdout);
            if (stderr.Length > 0) Console.Error.WriteLine(stderr);
            return new BundleResult(proc.ExitCode, "");
        }

        var outputName = ReadBundleOutputName(metaFile, distDir);
        if (outputName is null)
        {
            Console.Error.WriteLine("razorshave: could not read esbuild metafile — bundle output name unknown");
            return new BundleResult(ExitEsbuildMetaMissing, "");
        }
        return new BundleResult(0, outputName);
    }

    private static string? ReadBundleOutputName(string metaFile, string distDir)
    {
        if (!File.Exists(metaFile)) return null;
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(metaFile));
        if (!doc.RootElement.TryGetProperty("outputs", out var outputs)) return null;
        foreach (var output in outputs.EnumerateObject())
        {
            // Keys are paths relative to cwd; pick the first .js.
            if (output.Name.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileName(output.Name);
            }
        }
        return null;
    }

    private static void FinaliseDist(
        string distDir,
        string projectDir,
        string runtimeStaging,
        string bundleFileName,
        string configuration,
        string tfm)
    {
        // Drop individual component js + main.js + runtime/ — the bundle has
        // them all inlined. Keep only the hashed bundle and the index.html.
        foreach (var js in Directory.EnumerateFiles(distDir, "*.js", SearchOption.TopDirectoryOnly))
        {
            if (!Path.GetFileName(js).Equals(bundleFileName, StringComparison.Ordinal))
            {
                File.Delete(js);
            }
        }
        var metaFile = Path.Combine(distDir, ".esbuild-meta.json");
        if (File.Exists(metaFile)) File.Delete(metaFile);
        if (Directory.Exists(runtimeStaging)) Directory.Delete(runtimeStaging, recursive: true);

        // Copy wwwroot/ so static assets (favicon, CSS, images, bootstrap)
        // ship next to the bundle.
        var wwwroot = Path.Combine(projectDir, "wwwroot");
        if (Directory.Exists(wwwroot))
        {
            CopyDirectory(wwwroot, distDir);
        }

        // Razor scoped-CSS bundle lives under obj/<config>/<tfm>/scopedcss/bundle/
        // as <AssemblyName>.styles.css. Copy it alongside the other assets.
        var scopedCssLink = CopyScopedCssBundle(distDir, projectDir, configuration, tfm);

        WriteIndexHtml(distDir, bundleFileName, BuildCssLinks(distDir, scopedCssLink));
        WriteDeployConfigs(distDir);
    }

    /// <summary>
    /// Emit host-specific fallback configs so the dist/ runs untouched on the
    /// most common static hosts. Each file is ignored by the hosts it wasn't
    /// written for, so shipping all of them is harmless.
    /// </summary>
    private static void WriteDeployConfigs(string distDir)
    {
        // npx serve (local dev): reads serve.json by convention.
        File.WriteAllText(Path.Combine(distDir, "serve.json"),
            "{\n  \"rewrites\": [{ \"source\": \"**\", \"destination\": \"/index.html\" }]\n}\n");

        // Netlify / Cloudflare Pages: plain-text redirects file.
        File.WriteAllText(Path.Combine(distDir, "_redirects"),
            "/*    /index.html   200\n");

        // IIS / Azure Static Web Apps: rewrite all non-file, non-directory
        // requests to /index.html so the client router handles them.
        const string webConfig = """
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <rewrite>
      <rules>
        <rule name="Razorshave SPA fallback" stopProcessing="true">
          <match url=".*" />
          <conditions logicalGrouping="MatchAll">
            <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
            <add input="{REQUEST_FILENAME}" matchType="IsDirectory" negate="true" />
          </conditions>
          <action type="Rewrite" url="/index.html" />
        </rule>
      </rules>
    </rewrite>
  </system.webServer>
</configuration>
""";
        File.WriteAllText(Path.Combine(distDir, "web.config"), webConfig);

        // GitHub Pages: a 404.html identical to index.html gets served for any
        // unknown path. JS runs, reads window.location.pathname, router takes
        // it from there — no session-storage hack needed.
        var indexPath = Path.Combine(distDir, "index.html");
        if (File.Exists(indexPath))
        {
            File.Copy(indexPath, Path.Combine(distDir, "404.html"), overwrite: true);
        }
    }

    private static string? CopyScopedCssBundle(string distDir, string projectDir, string configuration, string tfm)
    {
        var scopedDir = Path.Combine(projectDir, "obj", configuration, tfm, "scopedcss", "bundle");
        if (!Directory.Exists(scopedDir)) return null;
        var bundleFile = Directory.EnumerateFiles(scopedDir, "*.styles.css").FirstOrDefault();
        if (bundleFile is null) return null;

        var name = Path.GetFileName(bundleFile);
        File.Copy(bundleFile, Path.Combine(distDir, name), overwrite: true);
        return name;
    }

    private static List<string> BuildCssLinks(string distDir, string? scopedCssLink)
    {
        // Order matches Blazor's App.razor convention: framework CSS first,
        // app CSS second, scoped CSS last so user component styles win.
        var links = new List<string>();
        if (File.Exists(Path.Combine(distDir, "lib", "bootstrap", "dist", "css", "bootstrap.min.css")))
            links.Add("lib/bootstrap/dist/css/bootstrap.min.css");
        if (File.Exists(Path.Combine(distDir, "app.css")))
            links.Add("app.css");
        if (scopedCssLink is not null)
            links.Add(scopedCssLink);
        return links;
    }

    // --- Path helpers ---

    private static string? FindCsproj(string projectDir)
    {
        return Directory.EnumerateFiles(projectDir, "*.csproj", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
    }

    private static string? FindGeneratedRazorRoot(string projectDir, string configuration, string tfm)
    {
        var candidate = Path.Combine(projectDir, "obj", configuration, tfm, "generated",
            "Microsoft.CodeAnalysis.Razor.Compiler",
            "Microsoft.NET.Sdk.Razor.SourceGenerators.RazorSourceGenerator");
        return Directory.Exists(candidate) ? candidate : null;
    }
}
