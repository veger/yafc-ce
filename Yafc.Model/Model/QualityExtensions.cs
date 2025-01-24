using System.Diagnostics.CodeAnalysis;

namespace Yafc.Model;

public static class QualityExtensions {
    public static float GetCraftingSpeed(this IObjectWithQuality<EntityCrafter> crafter) => crafter.target.CraftingSpeed(crafter.quality);

    public static float GetPower(this IObjectWithQuality<Entity> entity) => entity.target.Power(entity.quality);

    public static float GetBeaconEfficiency(this IObjectWithQuality<EntityBeacon> beacon) => beacon.target.BeaconEfficiency(beacon.quality);

    public static float GetAttractorEfficiency(this IObjectWithQuality<EntityAttractor> attractor)
        => attractor.target.Efficiency(attractor.quality);

    public static float StormPotentialPerTick(this IObjectWithQuality<EntityAttractor> attractor)
        => attractor.target.StormPotentialPerTick(attractor.quality);

    /// <summary>
    /// If possible, converts an <see cref="IObjectWithQuality{T}"/> into one with a different generic parameter.
    /// </summary>
    /// <typeparam name="T">The desired type parameter for the output <see cref="IObjectWithQuality{T}"/>.</typeparam>
    /// <param name="obj">The input <see cref="IObjectWithQuality{T}"/> to be converted.</param>
    /// <param name="result">If <c><paramref name="obj"/>?.target is <typeparamref name="T"/></c>, an <see cref="IObjectWithQuality{T}"/> with
    /// the same target and quality as <paramref name="obj"/>. Otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the conversion was successful, or <see langword="false"/> if it was not.</returns>
    public static bool Is<T>(this IObjectWithQuality<FactorioObject>? obj, [NotNullWhen(true)] out IObjectWithQuality<T>? result) where T : FactorioObject {
        if (obj is null or IObjectWithQuality<T>) {
            result = obj as IObjectWithQuality<T>;
            return result is not null;
        }
        // Use the conversion because it permits a null target. The constructor does not.
        result = (ObjectWithQuality<T>?)(obj.target as T, obj.quality);
        return result is not null;
    }
}
