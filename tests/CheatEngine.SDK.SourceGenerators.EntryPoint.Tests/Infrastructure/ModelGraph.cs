using System.Collections;
using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Infrastructure;

/// <summary>
///     Walks the object graph of a pipeline value and fails on anything that must never be cached between generator
///     runs: symbols, syntax, locations, semantic models, compilations. Such objects are not value-equatable (the step
///     would never be <c>Unchanged</c>) and they keep a whole compilation alive.
/// </summary>
internal static class ModelGraph
{
    private static readonly Type[] ForbiddenTypes =
    [
        typeof(ISymbol),
        typeof(SyntaxNode),
        typeof(SyntaxTree),
        typeof(SyntaxToken),
        typeof(SyntaxReference),
        typeof(Location),
        typeof(SemanticModel),
        typeof(Compilation),
        typeof(AttributeData)
    ];

    /// <summary>Asserts that <paramref name="value" /> reaches no Roslyn object; returns the number of objects visited.</summary>
    public static int AssertFreeOfRoslynObjects(object? value, string stepName)
    {
        HashSet<object> visited = new(ReferenceEqualityComparer.Instance);
        Visit(value, stepName, visited, 0);
        return visited.Count;
    }

    private static void Visit(object? value, string path, HashSet<object> visited, int depth)
    {
        if (value is null) return;

        var type = value.GetType();
        Assert.False(
            Array.Exists(ForbiddenTypes, forbidden => forbidden.IsAssignableFrom(type)),
            $"{path}: a {type.FullName} is held by the pipeline model.");

        if (type.IsPrimitive || type.IsEnum || value is string) return;

        Assert.True(depth < 32, $"{path}: object graph too deep.");
        if (!type.IsValueType && !visited.Add(value)) return;

        if (value is IEnumerable sequence)
        {
            var index = 0;
            foreach (var item in sequence)
                Visit(item, $"{path}[{index++.ToString(CultureInfo.InvariantCulture)}]", visited, depth + 1);

            return;
        }

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            Visit(field.GetValue(value), $"{path}.{field.Name}", visited, depth + 1);
    }
}
