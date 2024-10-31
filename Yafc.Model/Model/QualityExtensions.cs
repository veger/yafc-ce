namespace Yafc.Model;

public static class QualityExtensions {
    public static float GetCraftingSpeed(this IObjectWithQuality<EntityCrafter> crafter) => crafter.target.CraftingSpeed(crafter.quality);

    public static float GetPower(this IObjectWithQuality<Entity> entity) => entity.target.Power(entity.quality);
}
