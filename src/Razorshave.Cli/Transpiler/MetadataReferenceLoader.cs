using System.Runtime.InteropServices;

using Microsoft.CodeAnalysis;

namespace Razorshave.Cli.Transpiler;

/// <summary>
/// Loads the <see cref="MetadataReference"/>s needed to build a
/// <see cref="Microsoft.CodeAnalysis.CSharp.CSharpCompilation"/> that can
/// resolve user-code types against the .NET shared framework.
/// </summary>
/// <remarks>
/// <para>
/// In the current M0 test-driven setup the transpiler runs in-process, so we
/// scan the runtime's own shared-framework directories (<c>Microsoft.NETCore.App</c>
/// and the matching-major <c>Microsoft.AspNetCore.App</c>). This is enough for
/// the fixtures: they reference <c>RenderTreeBuilder</c>, <c>ComponentBase</c>,
/// <c>MouseEventArgs</c>, etc., all of which live in those two folders.
/// </para>
/// <para>
/// In step 5.13 Razorshave runs as an MSBuild task — references then come from
/// <c>@(ReferencePath)</c> and this scanner is no longer used. The helper stays
/// around for tooling (explorer, fixture-rebuild scripts).
/// </para>
/// <para>
/// Results are cached for the process lifetime; loading 300+ assemblies once is
/// fine, doing it per <see cref="Transpiler.Transpile"/> call is wasteful.
/// </para>
/// </remarks>
public static class MetadataReferenceLoader
{
    // Cached references + the runtime-directory they were loaded from. If
    // the runtime-directory changes between calls (long-lived MSBuild
    // worker that rebuilt against a new SDK, tests swapping runtime path)
    // we discard the cache and reload — otherwise SemanticModel would
    // silently resolve user types against stale framework assemblies.
    private static IReadOnlyList<MetadataReference>? _cachedSharedFramework;
    private static string? _cachedForRuntimeDir;

    public static IReadOnlyList<MetadataReference> SharedFramework()
    {
        var runtimeDir = Path.TrimEndingDirectorySeparator(RuntimeEnvironment.GetRuntimeDirectory());
        if (_cachedSharedFramework is not null && _cachedForRuntimeDir == runtimeDir)
        {
            return _cachedSharedFramework;
        }
        _cachedForRuntimeDir = runtimeDir;
        _cachedSharedFramework = LoadSharedFramework();
        return _cachedSharedFramework;
    }

    /// <summary>
    /// Clears the cached reference list. Intended for tests that need to
    /// exercise the load path in isolation, or for long-lived processes
    /// that want to pick up an SDK swap without restarting.
    /// </summary>
    public static void Reset()
    {
        _cachedSharedFramework = null;
        _cachedForRuntimeDir = null;
    }

    private static List<MetadataReference> LoadSharedFramework()
    {
        var refs = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAdd(string dll)
        {
            if (!seen.Add(dll)) return;
            try
            {
                refs.Add(MetadataReference.CreateFromFile(dll));
            }
            catch (Exception ex)
            {
                // A malformed or locked DLL would silently drop out of the
                // reference list before — any user type it carries would then
                // be unresolved in SemanticModel, and the transpiler would
                // quietly emit wrong JS (event subscriptions not rewritten,
                // `[JsonPropertyName]` attributes not honoured, etc.). Log so
                // the degradation is visible.
                Console.Error.WriteLine($"razorshave: skipping unreadable reference {dll}: {ex.Message}");
            }
        }

        // Microsoft.NETCore.App — strip the trailing separator so directory-math
        // lands one level higher rather than sticking at the same folder.
        var netcoreDir = Path.TrimEndingDirectorySeparator(RuntimeEnvironment.GetRuntimeDirectory());
        foreach (var dll in Directory.GetFiles(netcoreDir, "*.dll"))
        {
            TryAdd(dll);
        }

        // Microsoft.AspNetCore.App — pick the highest-version folder whose
        // major matches the running NETCore.App. Substring matching fails on
        // "10.0.5" vs "9.0.9", so compare Version values.
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
                {
                    foreach (var dll in Directory.GetFiles(match.Path, "*.dll"))
                    {
                        TryAdd(dll);
                    }
                }
            }
        }

        return refs;
    }

    private static Version? ParseVersion(string s)
    {
        var head = s.Split('-')[0];
        return Version.TryParse(head, out var v) ? v : null;
    }
}
