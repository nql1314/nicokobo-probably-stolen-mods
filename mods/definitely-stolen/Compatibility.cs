using System.Collections.Generic;

namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }
namespace DefinitelyStolen
{
    internal static class Numbers
    {
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
    internal static class EnumerableCompatibility
    {
        internal static IEnumerable<T> DistinctBy<T, TKey>(this IEnumerable<T> values, Func<T, TKey> key)
        { var seen = new HashSet<TKey>(); foreach (var value in values) if (seen.Add(key(value))) yield return value; }
    }
}
