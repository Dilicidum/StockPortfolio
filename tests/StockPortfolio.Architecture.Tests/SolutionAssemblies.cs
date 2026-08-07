using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace StockPortfolio.Tests;

internal static class SolutionAssemblies
{
    private const string ModulePrefix = "StockPortfolio.Modules.";

    public const string ContractsLayer = "Contracts";

    public static ImmutableArray<string> ModuleNames { get; } =
        ["Alerts", "Identity", "Portfolio", "MarketData"];

    public static ImmutableArray<string> LayerNames { get; } =
        [ContractsLayer, "Domain", "Application", "Infrastructure", "Api"];

    public static ImmutableArray<string> ExpectedNames { get; } = BuildExpectedNames();

    public static ImmutableArray<string> ScannedNames { get; } = BuildScannedNames();

    public static string NameOf(string module, string layer) => ModulePrefix + module + "." + layer;

    public static Assembly Get(string simpleName) => Assembly.Load(new AssemblyName(simpleName));

    public static bool IsFirstParty([NotNullWhen(true)] string? name) =>
        name is not null
        && name.StartsWith("StockPortfolio.", StringComparison.Ordinal)
        && !name.EndsWith(".Tests", StringComparison.Ordinal);

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

    public static bool IsEmptyShell(Assembly assembly) =>
        !assembly.GetTypes().Any(type =>
            type.Namespace?.StartsWith("StockPortfolio", StringComparison.Ordinal) == true);

    public static bool HasCode(string simpleName) => !IsEmptyShell(Get(simpleName));

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
