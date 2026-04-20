using System.Diagnostics;
using System.Globalization;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
            return 2;
        }

        var csproj = FindCsproj(absProject);
        if (csproj is null)
        {
            Console.Error.WriteLine($"razorshave: no .csproj found in {absProject}");
            return 3;
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

        var generatedRoot = FindGeneratedRazorRoot(absProject, configuration);
        if (generatedRoot is null)
        {
            Console.Error.WriteLine($"razorshave: expected Razor-generated sources under obj/{configuration}/ — did EmitCompilerGeneratedFiles fail?");
            return 4;
        }

        var distDir = Path.Combine(absProject, "dist");
        if (Directory.Exists(distDir)) Directory.Delete(distDir, recursive: true);
        Directory.CreateDirectory(distDir);

        Console.WriteLine("[2/6] Transpiling components ...");
        var (components, routesConfig) = TranspileAll(generatedRoot, distDir, absProject, configuration);
        if (components.Count == 0)
        {
            Console.Error.WriteLine("razorshave: no component classes found");
            return 5;
        }

        Console.WriteLine("[3/6] Copying runtime ...");
        var runtimeSrc = FindRuntimeSource();
        if (runtimeSrc is null)
        {
            Console.Error.WriteLine("razorshave: runtime source directory not found");
            return 6;
        }
        var runtimeStaging = Path.Combine(distDir, "runtime");
        CopyDirectory(runtimeSrc, runtimeStaging);

        Console.WriteLine("[4/6] Writing main.js entry ...");
        WriteAppJs(distDir, absProject, components, routesConfig);

        Console.WriteLine("[5/6] Bundling with esbuild ...");
        var esbuild = FindEsbuildBinary();
        if (esbuild is null)
        {
            Console.Error.WriteLine("razorshave: esbuild not found in Razorshave.Runtime/node_modules/.bin/ — run `npm install` in the runtime project.");
            return 7;
        }
        var bundleResult = RunEsbuild(esbuild, distDir, runtimeStaging);
        if (bundleResult.Exit != 0) return bundleResult.Exit;

        Console.WriteLine("[6/6] Finalising dist/ (prune unbundled sources, copy wwwroot + scoped CSS) ...");
        FinaliseDist(distDir, absProject, runtimeStaging, bundleResult.OutputFileName, configuration);

        Console.WriteLine();
        var routedCount = components.Count(c => c.RoutePatterns.Count > 0);
        Console.WriteLine($"✓ razorshave: {components.Count} component(s), {routedCount} routed → {distDir}");
        foreach (var c in components)
        {
            var marker = c.RoutePatterns.Count > 0 ? $"  [{string.Join(", ", c.RoutePatterns)}]" : "";
            Console.WriteLine($"    {c.Name}.js{marker}");
        }
        if (routesConfig.DefaultLayout is not null)
            Console.WriteLine($"    DefaultLayout: {routesConfig.DefaultLayout}");
        if (routesConfig.NotFound is not null)
            Console.WriteLine($"    NotFound: {routesConfig.NotFound}");
        return 0;
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
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
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

    private static (List<TranspiledComponent> components, RouteExtractor.RoutesConfig routesConfig)
        TranspileAll(string generatedRoot, string distDir, string projectDir, string configuration)
    {
        var components = new List<TranspiledComponent>();
        var routesConfig = RouteExtractor.RoutesConfig.Empty;

        // Feed the user project's referenced assemblies into every per-file
        // compilation. Without this, SemanticModel can't resolve types like
        // `IStore<T>` from Razorshave.Abstractions — event-symbol detection
        // silently falls through and patterns like `Store.OnChange += X`
        // stay as raw C# text in the JS output.
        var projectRefs = LoadProjectBinReferences(projectDir, configuration);

        foreach (var genFile in Directory.EnumerateFiles(generatedRoot, "*_razor.g.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(genFile);
            var tree = CSharpSyntaxTree.ParseText(source);
            var componentCls = FindComponentClass(tree);
            if (componentCls is null) continue;

            var className = componentCls.Identifier.Text;

            // Routes.razor is not transpiled — its role (hosting Blazor's
            // Router) is replaced by the runtime Router we wire up in main.js.
            // We do peek at it for DefaultLayout / NotFoundPage metadata.
            if (className == "Routes")
            {
                routesConfig = RouteExtractor.ExtractRoutesConfig(tree);
                continue;
            }

            var js = Transpile(source, projectRefs);
            if (string.IsNullOrWhiteSpace(js)) continue;

            var outFile = Path.Combine(distDir, $"{className}.js");
            File.WriteAllText(outFile, js);

            var patterns = RouteExtractor.ExtractRoutePatterns(componentCls);
            components.Add(new TranspiledComponent(className, outFile, patterns));
        }

        return (
            components.OrderBy(c => c.Name, StringComparer.Ordinal).ToList(),
            routesConfig);
    }

    private static List<MetadataReference> LoadProjectBinReferences(string projectDir, string configuration)
    {
        // Scan the user project's output bin for DLLs other than the SDK-shared
        // ones (those already live in MetadataReferenceLoader.SharedFramework()).
        // This is a pragmatic stand-in for reading @(ReferencePath) from MSBuild;
        // revisit if the signal gets noisy.
        var binDir = Path.Combine(projectDir, "bin", configuration, "net10.0");
        if (!Directory.Exists(binDir)) return [];
        var refs = new List<MetadataReference>();
        foreach (var dll in Directory.GetFiles(binDir, "*.dll", SearchOption.TopDirectoryOnly))
        {
            try { refs.Add(MetadataReference.CreateFromFile(dll)); }
            catch { /* unreadable dll — skip */ }
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
    {
        if (cls.BaseList is null || cls.BaseList.Types.Count == 0) return false;
        var raw = cls.BaseList.Types[0].Type.ToString();
        var lastDot = raw.LastIndexOf('.');
        var simple = lastDot < 0 ? raw : raw[(lastDot + 1)..];
        return simple is "ComponentBase" or "LayoutComponentBase";
    }

    // --- Runtime + Assets ---

    private static string? FindRuntimeSource()
    {
        // Two search roots cover dev, test and MSBuild-task contexts:
        //   * the assembly's own directory (CLI bin, test bin, MSBuild-loaded copy)
        //   * the current working directory (a repo-root `dotnet run` or `dotnet test`)
        // From each we walk up and look for `[…]/src/Razorshave.Runtime/src`.
        var roots = new List<string>();
        var asmPath = typeof(BuildCommand).Assembly.Location;
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
        RouteExtractor.RoutesConfig routesConfig)
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
        sb.AppendLine("import { Component, h, mount, Router } from '@razorshave/runtime';");
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
        var runtimeSrc = FindRuntimeSource();
        if (runtimeSrc is null) return null;

        // Runtime src is .../Razorshave.Runtime/src/ — .bin is next to src/.
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
        psi.ArgumentList.Add("--tree-shaking=true");
        psi.ArgumentList.Add($"--outdir={distDir}");
        psi.ArgumentList.Add("--entry-names=[name].[hash]");
        psi.ArgumentList.Add($"--alias:@razorshave/runtime={Path.Combine(runtimeDir, "index.js")}");
        psi.ArgumentList.Add($"--metafile={metaFile}");

        var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
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
            return new BundleResult(8, "");
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
        string configuration)
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
        var scopedCssLink = CopyScopedCssBundle(distDir, projectDir, configuration);

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

    private static string? CopyScopedCssBundle(string distDir, string projectDir, string configuration)
    {
        var scopedDir = Path.Combine(projectDir, "obj", configuration, "net10.0", "scopedcss", "bundle");
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

    private static string? FindGeneratedRazorRoot(string projectDir, string configuration)
    {
        var candidate = Path.Combine(projectDir, "obj", configuration, "net10.0", "generated",
            "Microsoft.CodeAnalysis.Razor.Compiler",
            "Microsoft.NET.Sdk.Razor.SourceGenerators.RazorSourceGenerator");
        return Directory.Exists(candidate) ? candidate : null;
    }
}
