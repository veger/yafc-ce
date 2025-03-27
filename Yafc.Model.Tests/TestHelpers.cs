using System.Collections.Generic;
using System.Linq;

namespace Yafc.Model.Tests;

internal static class TestHelpers {
    /// <summary>
    /// Produces the <see cref="IObjectWithQuality{T}"/>s corresponding to each pair of 'input value' and 'defined quality'.
    /// </summary>
    /// <typeparam name="T">The type of the input objects. Must be <see cref="FactorioObject"/> or one of its subclasses.</typeparam>
    /// <param name="values">The sequence of values that should have qualities applied to them.</param>
    /// <returns>The cartesian product of <paramref name="values"/> and the members of <see cref="Database.qualities"/>.</returns>
    public static IEnumerable<IObjectWithQuality<T>> WithAllQualities<T>(this IEnumerable<T> values) where T : FactorioObject
        => values.SelectMany(c => Database.qualities.all.Select(q => c.With(q))).Distinct();
}
