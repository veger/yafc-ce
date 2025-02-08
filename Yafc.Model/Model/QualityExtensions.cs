using System.Diagnostics.CodeAnalysis;

namespace Yafc.Model;

public static class QualityExtensions {
    // This method cannot be declared on the interface.
    public static void Deconstruct<T>(this IObjectWithQuality<T> value, out T obj, out Quality quality) where T : FactorioObject {
        obj = value.target;
        quality = value.quality;
    }

    public static float GetCraftingSpeed(this IObjectWithQuality<EntityCrafter> crafter) => crafter.target.CraftingSpeed(crafter.quality);

    public static float GetPower(this IObjectWithQuality<Entity> entity) => entity.target.Power(entity.quality);

    public static float GetBeaconEfficiency(this IObjectWithQuality<EntityBeacon> beacon) => beacon.target.BeaconEfficiency(beacon.quality);

    public static float GetAttractorEfficiency(this IObjectWithQuality<EntityAttractor> attractor)
        => attractor.target.Efficiency(attractor.quality);

    public static float StormPotentialPerTick(this IObjectWithQuality<EntityAttractor> attractor)
        => attractor.target.StormPotentialPerTick(attractor.quality);

    public static bool IsSourceResource(this IObjectWithQuality<Goods>? goods) => (goods?.target).IsSourceResource();
    /// <summary>
    /// Gets the string <c>target.name + "@" + quality.name</c>.
    /// </summary>
    public static string QualityName(this IObjectWithQuality<FactorioObject> obj) => obj.target.name + "@" + obj.quality.name;

    public static IObjectWithQuality<Goods>? mainProduct(this IObjectWithQuality<RecipeOrTechnology> recipe) =>
        (ObjectWithQuality<Goods>?)(recipe.target.mainProduct, recipe.quality);

    public static bool CanAcceptModule(this IObjectWithQuality<RecipeOrTechnology> recipe, Module module) =>
        recipe.target.CanAcceptModule(module);
    public static bool CanAcceptModule(this IObjectWithQuality<RecipeOrTechnology> recipe, IObjectWithQuality<Module> module) =>
        recipe.target.CanAcceptModule(module.target);

    public static IObjectWithQuality<Item>? FuelResult(this IObjectWithQuality<Goods>? goods) =>
        (goods?.target as Item)?.fuelResult.With(goods!.quality); // null-forgiving: With is not called if goods is null.

    [return: NotNullIfNotNull(nameof(obj))]
    public static ObjectWithQuality<T>? With<T>(this T? obj, Quality quality) where T : FactorioObject => (obj, quality);

    /// <summary>
    /// If possible, converts an <see cref="IObjectWithQuality{T}"/> into one with a different generic parameter.
    /// </summary>
    /// <typeparam name="T">The desired type parameter for the output <see cref="IObjectWithQuality{T}"/>.</typeparam>
    /// <param name="obj">The input <see cref="IObjectWithQuality{T}"/> to be converted.</param>
    /// <param name="result">If <c><paramref name="obj"/>?.target is <typeparamref name="T"/></c>, an <see cref="IObjectWithQuality{T}"/> with
    /// the same target and quality as <paramref name="obj"/>. Otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the conversion was successful, or <see langword="false"/> if it was not.</returns>
    public static bool Is<T>(this IObjectWithQuality<FactorioObject>? obj, [NotNullWhen(true)] out IObjectWithQuality<T>? result) where T : FactorioObject {
        result = obj.As<T>();
        return result is not null;
    }

    /// <summary>
    /// Tests whether an <see cref="IObjectWithQuality{T}"/> can be converted to one with a different generic parameter.
    /// </summary>
    /// <typeparam name="T">The desired type parameter for the <see cref="IObjectWithQuality{T}"/> conversion to be tested.</typeparam>
    /// <param name="obj">The input <see cref="IObjectWithQuality{T}"/> to be converted.</param>
    /// <returns><see langword="true"/> if the conversion is possible, or <see langword="false"/> if it was not.</returns>
    public static bool Is<T>(this IObjectWithQuality<FactorioObject>? obj) where T : FactorioObject => obj?.target is T;

    /// <summary>
    /// If possible, converts an <see cref="IObjectWithQuality{T}"/> into one with a different generic parameter.
    /// </summary>
    /// <typeparam name="T">The desired type parameter for the output <see cref="IObjectWithQuality{T}"/>.</typeparam>
    /// <param name="obj">The input <see cref="IObjectWithQuality{T}"/> to be converted.</param>
    /// <param name="result">If <c><paramref name="obj"/>?.target is <typeparamref name="T"/></c>, an <see cref="IObjectWithQuality{T}"/> with
    /// the same target and quality as <paramref name="obj"/>. Otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the conversion was successful, or <see langword="false"/> if it was not.</returns>
    public static IObjectWithQuality<T>? As<T>(this IObjectWithQuality<FactorioObject>? obj) where T : FactorioObject {
        if (obj is null or IObjectWithQuality<T>) {
            return obj as IObjectWithQuality<T>;
        }
        // Use the conversion because it permits a null target. The constructor does not.
        return (ObjectWithQuality<T>?)(obj.target as T, obj.quality);
    }
}
