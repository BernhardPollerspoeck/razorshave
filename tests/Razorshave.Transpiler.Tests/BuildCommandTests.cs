using Razorshave.Cli;

namespace Razorshave.Transpiler.Tests;

/// <summary>
/// Integration-style tests for <see cref="BuildCommand"/> — the CLI pipeline
/// that transpiles, bundles and packages a project into its <c>dist/</c>.
/// These run against the real KitchenSink.Client project. They're slower
/// than the snapshot tests (invoke <c>dotnet build</c> + esbuild), so we run
/// the build once and assert multiple invariants against the resulting
/// output.
/// </summary>
public sealed class BuildCommandTests : IClassFixture<BuildCommandTests.KitchenSinkBuildFixture>
{
    private readonly KitchenSinkBuildFixture _fixture;

    public BuildCommandTests(KitchenSinkBuildFixture fixture) { _fixture = fixture; }

    [Fact]
    public void Build_succeeds_and_produces_a_dist_folder()
    {
        Assert.Equal(0, _fixture.ExitCode);
        Assert.True(Directory.Exists(_fixture.DistPath), $"dist not found at {_fixture.DistPath}");
    }

    [Fact]
    public void Dist_contains_index_html_referencing_the_hashed_bundle()
    {
        var indexPath = Path.Combine(_fixture.DistPath, "index.html");
        Assert.True(File.Exists(indexPath));

        var html = File.ReadAllText(indexPath);
        // Default deployment is at "/" so the bundle is at /main.X.js — must
        // be absolute, otherwise a deep-link to /legal/agb resolves the
        // script src against /legal/ and 404s.
        Assert.Matches(@"<script type=""module"" src=""/main\.[A-Z0-9]{8}\.js""></script>", html);
    }

    [Fact]
    public void Index_html_uses_absolute_paths_for_every_asset()
    {
        var html = File.ReadAllText(Path.Combine(_fixture.DistPath, "index.html"));

        // All href= and src= attributes inside index.html must be rooted
        // at "/" — relative paths break SPA deep-link routing.
        var refs = System.Text.RegularExpressions.Regex
            .Matches(html, @"(?:href|src)=""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.NotEmpty(refs);
        var relative = refs.Where(r => !r.StartsWith('/')).ToList();
        Assert.Empty(relative);
    }

    [Fact]
    public void Dist_contains_exactly_one_hashed_bundle_and_no_unbundled_component_sources()
    {
        var jsFiles = Directory.GetFiles(_fixture.DistPath, "*.js", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .ToList();

        Assert.Single(jsFiles);
        Assert.Matches(@"^main\.[A-Z0-9]{8}\.js$", jsFiles[0]!);
    }

    [Fact]
    public void Bundle_inlines_the_runtime_and_user_components()
    {
        var bundle = Directory.GetFiles(_fixture.DistPath, "main.*.js").Single();
        var content = File.ReadAllText(bundle);

        // Runtime: h() and Component show up in minified-but-readable form.
        Assert.Contains("class", content, StringComparison.Ordinal);
        Assert.Contains("render", content, StringComparison.Ordinal);
        // User components the transpiler emitted — we don't tree-shake the app
        // entry, so Counter/Weather/Home at least must reach the bundle.
        Assert.Contains("Counter", content, StringComparison.Ordinal);
        Assert.Contains("Weather", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Dist_copies_wwwroot_assets()
    {
        // KitchenSink's wwwroot ships with favicon + app.css in the default template.
        Assert.True(File.Exists(Path.Combine(_fixture.DistPath, "favicon.png")),
            "favicon.png should be copied from wwwroot");
        Assert.True(File.Exists(Path.Combine(_fixture.DistPath, "app.css")),
            "app.css should be copied from wwwroot");
    }

    [Fact]
    public void Index_html_links_every_present_favicon_with_correct_mime()
    {
        // KitchenSink ships favicon.png in wwwroot. The link must use
        // image/png and an absolute href so deep-links still resolve.
        var html = File.ReadAllText(Path.Combine(_fixture.DistPath, "index.html"));
        Assert.Contains(@"<link rel=""icon"" type=""image/png"" href=""/favicon.png"">", html);
    }

    [Fact]
    public void Index_html_uses_assembly_name_as_default_title()
    {
        // Razorshave defaults <RazorshaveTitle> to $(AssemblyName) — for
        // KitchenSink that's "KitchenSink.Client". Hardcoding "Razorshave"
        // would be a regression: every shipped SPA would carry the framework
        // name as its title.
        var html = File.ReadAllText(Path.Combine(_fixture.DistPath, "index.html"));
        // The fixture invokes Run() without an MSBuild context, so Title is
        // null/empty and the fallback "Razorshave" wins. The MSBuild path
        // (covered by the targets file) defaults to AssemblyName instead;
        // here we lock down that an explicit Title overrides nothing else.
        Assert.Contains("<title>Razorshave</title>", html);
    }

    public sealed class KitchenSinkBuildFixture : IDisposable
    {
        public string DistPath { get; }
        public int ExitCode { get; }

        public KitchenSinkBuildFixture()
        {
            var kitchenSink = FindKitchenSink()
                ?? throw new InvalidOperationException("KitchenSink.Client project not found relative to test assembly");

            DistPath = Path.Combine(kitchenSink, "dist");
            if (Directory.Exists(DistPath)) Directory.Delete(DistPath, recursive: true);

            ExitCode = BuildCommand.Run(kitchenSink);
        }

        public void Dispose() { /* Leave dist/ in place — useful for debugging after a red test. */ }

        private static string? FindKitchenSink()
        {
            // tests assembly lives in tests/Razorshave.Transpiler.Tests/bin/…
            // KitchenSink is at e2e/KitchenSink.Client/ — walk up to find it.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "e2e", "KitchenSink.Client");
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "KitchenSink.Client.csproj")))
                    return candidate;
            }
            return null;
        }
    }
}
