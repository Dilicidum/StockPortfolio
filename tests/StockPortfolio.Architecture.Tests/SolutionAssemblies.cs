using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace StockPortfolio.Tests;

/// <summary>
/// Discovery and reference-graph helpers shared by the architecture rules.
/// </summary>
/// <remarks>
/// <para>
/// Every rule here reads <see cref="Assembly.GetReferencedAssemblies"/>, which is the AssemblyRef
/// table — the assemblies the compiler actually emitted a reference to. A <c>ProjectReference</c>
/// whose types are never used is trimmed out of that table, so an *absence* is never proof of
/// compliance. That cuts both ways: it makes the rules free of false positives, and it makes an
/// empty result set worthless as evidence. Hence <see cref="ExpectedNames"/> and the loadability
/// guard in <c>ModuleBoundaryTests</c> — if discovery ever returns nothing, that is a failure, not
/// a pass.
/// </para>
/// <para>
/// Assemblies are loaded explicitly by simple name rather than relying on whatever the test host
/// happened to touch first, for the same reason.
/// </para>
/// </remarks>
internal static class SolutionAssemblies
{
    private const string ModulePrefix = "StockPortfolio.Modules.";

    /// <summary>The one layer of a module every other module is allowed to see.</summary>
    public const string ContractsLayer = "Contracts";

    /// <summary>The four modules of the monolith.</summary>
    public static ImmutableArray<string> ModuleNames { get; } =
        ["Identity", "Portfolio", "MarketData", "Alerts"];

    /// <summary>The five projects each module is built from, innermost first.</summary>
    public static ImmutableArray<string> LayerNames { get; } =
        [ContractsLayer, "Domain", "Application", "Infrastructure", "Presentation"];

    /// <summary>
    /// The composition roots, exempt from the cross-module rule. Both reference every
    /// <c>&lt;M&gt;.Infrastructure</c> and <c>&lt;M&gt;.Presentation</c> by design — that is what a
    /// host is for. Without this exemption the rule is red the moment the host wires a module up.
    /// </summary>
    public static ImmutableArray<string> HostAssemblyNames { get; } =
        ["StockPortfolio.Api", "StockPortfolio.Migrator"];

    /// <summary>
    /// The assemblies that must exist for the rules below to mean anything. Twenty-two: four
    /// modules times five layers, plus the two shared projects.
    /// </summary>
    public static ImmutableArray<string> ExpectedNames { get; } = BuildExpectedNames();

    /// <summary>
    /// Everything the rules run over: <see cref="ExpectedNames"/> plus any other first-party
    /// assembly that turns up next to the test binary. The extras matter because the day
    /// <c>StockPortfolio.Api</c> lands in this folder it must be scanned — and exempted — rather
    /// than silently ignored.
    /// </summary>
    public static ImmutableArray<string> ScannedNames { get; } = BuildScannedNames();

    /// <summary>Composes the assembly name of one module layer.</summary>
    /// <param name="module">The module, for example <c>Identity</c>.</param>
    /// <param name="layer">The layer, for example <c>Domain</c>.</param>
    /// <returns>The simple assembly name.</returns>
    public static string NameOf(string module, string layer) => ModulePrefix + module + "." + layer;

    /// <summary>Loads an assembly by simple name, throwing if it is not there.</summary>
    /// <param name="simpleName">The simple assembly name.</param>
    /// <returns>The loaded assembly.</returns>
    public static Assembly Get(string simpleName) => Assembly.Load(new AssemblyName(simpleName));

    /// <summary>Reports whether an assembly name belongs to this solution.</summary>
    /// <param name="name">A simple assembly name, possibly <see langword="null"/>.</param>
    /// <returns><see langword="true"/> for a first-party, non-test assembly.</returns>
    public static bool IsFirstParty([NotNullWhen(true)] string? name) =>
        name is not null
        && name.StartsWith("StockPortfolio.", StringComparison.Ordinal)
        && !name.EndsWith(".Tests", StringComparison.Ordinal);

    /// <summary>Reports whether an assembly is one of the exempt composition roots.</summary>
    /// <param name="name">A simple assembly name.</param>
    /// <returns><see langword="true"/> for <c>Api</c> or <c>Migrator</c>.</returns>
    public static bool IsHost(string? name) =>
        name is not null && HostAssemblyNames.Contains(name, StringComparer.Ordinal);

    /// <summary>Splits a module assembly name into its module and layer.</summary>
    /// <param name="assemblyName">A simple assembly name, possibly <see langword="null"/>.</param>
    /// <param name="module">The module segment on success.</param>
    /// <param name="layer">The layer segment on success.</param>
    /// <returns><see langword="true"/> when the name is <c>StockPortfolio.Modules.M.L</c>.</returns>
    public static bool TryParseModuleLayer(
        string? assemblyName,
        [NotNullWhen(true)] out string? module,
        [NotNullWhen(true)] out string? layer)
    {
        module = null;
        layer = null;

        if (assemblyName is null || !assemblyName.StartsWith(ModulePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = assemblyName[ModulePrefix.Length..].Split('.');

        if (rest.Length != 2)
        {
            return false;
        }

        module = rest[0];
        layer = rest[1];

        return ModuleNames.Contains(module, StringComparer.Ordinal)
            && LayerNames.Contains(layer, StringComparer.Ordinal);
    }

    /// <summary>Reports whether a namespace sits under some module's <c>.Domain</c>.</summary>
    /// <param name="ns">The namespace, possibly <see langword="null"/>.</param>
    /// <returns><see langword="true"/> for <c>StockPortfolio.Modules.M.Domain[.*]</c>.</returns>
    public static bool IsDomainNamespace(string? ns)
    {
        if (ns is null || !ns.StartsWith(ModulePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var tail = ns[ModulePrefix.Length..];
        var dot = tail.IndexOf('.', StringComparison.Ordinal);

        if (dot < 0 || !ModuleNames.Contains(tail[..dot], StringComparer.Ordinal))
        {
            return false;
        }

        var afterModule = tail[(dot + 1)..];

        return string.Equals(afterModule, "Domain", StringComparison.Ordinal)
            || afterModule.StartsWith("Domain.", StringComparison.Ordinal);
    }

    /// <summary>
    /// Reports whether an assembly holds no code of ours yet — an empty shell project.
    /// </summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <returns><see langword="true"/> when it declares no <c>StockPortfolio.*</c> type.</returns>
    /// <remarks>
    /// <para>
    /// Not <c>GetTypes().Length == 0</c>: a shell is never quite empty. Every project here compiles
    /// with the OneOf source generator in scope, which injects
    /// <c>OneOf.GenerateOneOfAttribute</c> into the assembly, so a project with no <c>.cs</c> files
    /// still reports one type. Testing for zero types therefore never fires, and the reference
    /// rules then report a green earned by an AssemblyRef table containing nothing but
    /// <c>System.Runtime</c>.
    /// </para>
    /// <para>
    /// Rules skip such an assembly with a reason instead. They go live by themselves with the first
    /// first-party type the module gains.
    /// </para>
    /// </remarks>
    public static bool IsEmptyShell(Assembly assembly) =>
        !assembly.GetTypes().Any(type =>
            type.Namespace?.StartsWith("StockPortfolio", StringComparison.Ordinal) == true);

    /// <summary>
    /// Walks the first-party reference graph from <paramref name="rootName"/> and returns the
    /// shortest reference path to an assembly matching <paramref name="isForbidden"/>.
    /// </summary>
    /// <param name="rootName">The simple name of the assembly to start from.</param>
    /// <param name="isForbidden">Predicate over a referenced assembly's simple name.</param>
    /// <returns>
    /// The path, as <c>A -&gt; B -&gt; C</c>, or <see langword="null"/> when nothing matches.
    /// </returns>
    /// <remarks>
    /// Transitive, not just direct: <c>Infrastructure</c> pulling ASP.NET Core in by referencing
    /// its own <c>Presentation</c> is the same violation as referencing the framework outright, and
    /// a direct-only check would miss it. Traversal follows first-party edges only; the third-party
    /// closure is irrelevant and enormous.
    /// </remarks>
    public static string? FindForbiddenReferencePath(string rootName, Func<string, bool> isForbidden)
    {
        ArgumentNullException.ThrowIfNull(isForbidden);

        var predecessor = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [rootName] = null,
        };

        var queue = new Queue<string>();
        queue.Enqueue(rootName);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var reference in Get(current).GetReferencedAssemblies())
            {
                var name = reference.Name;

                if (name is null)
                {
                    continue;
                }

                if (isForbidden(name))
                {
                    predecessor[name] = current;
                    return DescribePath(predecessor, name);
                }

                if (IsFirstParty(name) && predecessor.TryAdd(name, current))
                {
                    queue.Enqueue(name);
                }
            }
        }

        return null;
    }

    private static string DescribePath(Dictionary<string, string?> predecessor, string leaf)
    {
        var path = new List<string>();

        for (string? node = leaf; node is not null; node = predecessor[node])
        {
            path.Add(node);
        }

        path.Reverse();

        return string.Join(" -> ", path);
    }

    private static ImmutableArray<string> BuildExpectedNames()
    {
        var names = ImmutableArray.CreateBuilder<string>();

        foreach (var module in ModuleNames)
        {
            foreach (var layer in LayerNames)
            {
                names.Add(NameOf(module, layer));
            }
        }

        names.Add("StockPortfolio.Shared.Kernel");
        names.Add("StockPortfolio.Shared.Presentation");

        return names.ToImmutable();
    }

    private static ImmutableArray<string> BuildScannedNames()
    {
        var found = Directory
            .EnumerateFiles(AppContext.BaseDirectory, "StockPortfolio.*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(IsFirstParty)
            .Select(name => name!);

        return [.. ExpectedNames.Union(found, StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }
}
