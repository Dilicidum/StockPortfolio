using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace StockPortfolio.Tests;

/// <summary>Discovery and reference-graph helpers shared by the architecture rules.</summary>
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
        [ContractsLayer, "Domain", "Application", "Infrastructure", "Api"];

    /// <summary>The composition roots, exempt from the cross-module rule.</summary>
    public static ImmutableArray<string> HostAssemblyNames { get; } =
        ["StockPortfolio.Api", "StockPortfolio.Migrator"];

    /// <summary>The assemblies that must exist for the rules below to mean anything.</summary>
    public static ImmutableArray<string> ExpectedNames { get; } = BuildExpectedNames();

    /// <summary>Everything the rules run over: ExpectedNames plus any other first-party assembly that turns up next.</summary>
    public static ImmutableArray<string> ScannedNames { get; } = BuildScannedNames();

    /// <summary>Composes the assembly name of one module layer.</summary>
    public static string NameOf(string module, string layer) => ModulePrefix + module + "." + layer;

    /// <summary>Loads an assembly by simple name, throwing if it is not there.</summary>
    public static Assembly Get(string simpleName) => Assembly.Load(new AssemblyName(simpleName));

    /// <summary>Reports whether an assembly name belongs to this solution.</summary>
    public static bool IsFirstParty([NotNullWhen(true)] string? name) =>
        name is not null
        && name.StartsWith("StockPortfolio.", StringComparison.Ordinal)
        && !name.EndsWith(".Tests", StringComparison.Ordinal);

    /// <summary>Reports whether an assembly is one of the exempt composition roots.</summary>
    public static bool IsHost(string? name) =>
        name is not null && HostAssemblyNames.Contains(name, StringComparer.Ordinal);

    /// <summary>Splits a module assembly name into its module and layer.</summary>
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

    /// <summary>Reports whether a namespace sits under some module's .Domain.</summary>
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

    /// <summary>Reports whether an assembly holds no code of ours yet — an empty shell project.</summary>
    public static bool IsEmptyShell(Assembly assembly) =>
        !assembly.GetTypes().Any(type =>
            type.Namespace?.StartsWith("StockPortfolio", StringComparison.Ordinal) == true);

    /// <summary>Walks the first-party reference graph from rootName and returns the shortest reference path to an.</summary>
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
        names.Add("StockPortfolio.Shared.Api");

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
