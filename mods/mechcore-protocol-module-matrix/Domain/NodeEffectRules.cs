namespace MechcoreProtocol.ModuleMatrix.Domain;

internal readonly record struct NodeStats(int Performance, int Efficiency,
    int Quality);

internal sealed record NodeClaimCandidate<TNode, TSource>(TNode Node,
    int ArrivalOrder, string StableId, IReadOnlyList<TSource> Sources)
    where TNode : notnull where TSource : notnull;

internal static class NodeEffectRules
{
    internal static IReadOnlyDictionary<TSource, TNode> FirstNodeClaims<
        TNode, TSource>(IEnumerable<NodeClaimCandidate<TNode, TSource>> nodes)
        where TNode : notnull where TSource : notnull
    {
        var claims = new Dictionary<TSource, TNode>();
        foreach (var node in nodes.OrderBy(x => x.ArrivalOrder)
                     .ThenBy(x => x.StableId, StringComparer.Ordinal))
            foreach (var source in node.Sources)
                claims.TryAdd(source, node.Node);
        return claims;
    }

    // Each neighboring source contributes one signed offset to its node.
    // Truncate each source before adding multiple contributions.
    internal static NodeStats ConversionContribution(string code,
        NodeStats source) => code switch
    {
        "N09" => new(checked(-source.Performance),
            Clamp(source.Performance / ModuleMatrixNumbers.Effects.ConversionSecondaryDivisor),
            Clamp(source.Performance / ModuleMatrixNumbers.Effects.ConversionQualityDivisor)),
        "N10" => new(Clamp(source.Efficiency / ModuleMatrixNumbers.Effects.ConversionSecondaryDivisor),
            checked(-source.Efficiency),
            Clamp(source.Efficiency / ModuleMatrixNumbers.Effects.ConversionQualityDivisor)),
        "N11" => new(source.Quality, source.Quality,
            checked(-source.Quality)),
        _ => default
    };

    internal static NodeStats Add(NodeStats left, NodeStats right) => new(
        checked(left.Performance + right.Performance),
        checked(left.Efficiency + right.Efficiency),
        checked(left.Quality + right.Quality));

    internal static int IsolatedSource(int source) => source > 0
        ? (int)((long)source * ModuleMatrixNumbers.Effects.IsolationPositiveNumerator / ModuleMatrixNumbers.Effects.IsolationDivisor)
        : (int)((long)source / ModuleMatrixNumbers.Effects.IsolationDivisor);

    internal static NodeStats Isolate(NodeStats source) => new(
        IsolatedSource(source.Performance),
        IsolatedSource(source.Efficiency),
        IsolatedSource(source.Quality));

    private static int Clamp(double value) =>
        (int)Math.Clamp(Math.Truncate(value), int.MinValue, int.MaxValue);
}
