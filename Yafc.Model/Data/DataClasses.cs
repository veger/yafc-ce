using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Yafc.UI;
[assembly: InternalsVisibleTo("Yafc.Parser")]

namespace Yafc.Model;

public interface IFactorioObjectWrapper {
    string text { get; }
    FactorioObject target { get; }
    float amount { get; }
}

internal enum FactorioObjectSortOrder {
    SpecialGoods,
    Items,
    Fluids,
    Recipes,
    Mechanics,
    Technologies,
    Entities,
    Tiles,
    Qualities,
}

public enum FactorioId { }

public abstract class FactorioObject : IFactorioObjectWrapper, IComparable<FactorioObject> {
    public string? factorioType { get; internal set; }
    public string name { get; internal set; } = null!; // null-forgiving: Initialized to non-null by GetObject.
    public string typeDotName => type + '.' + name;
    public string locName { get; internal set; } = null!; // null-forgiving: Copied from name if still null at the end of CalculateMaps
    public string? locDescr { get; internal set; }
    public FactorioIconPart[]? iconSpec { get; internal set; }
    public Icon icon { get; internal set; }
    public FactorioId id { get; internal set; }
    internal abstract FactorioObjectSortOrder sortingOrder { get; }
    public FactorioObjectSpecialType specialType { get; internal set; }
    public abstract string type { get; }
    FactorioObject IFactorioObjectWrapper.target => this;
    float IFactorioObjectWrapper.amount => 1f;

    string IFactorioObjectWrapper.text => locName;

    public void FallbackLocalization(FactorioObject? other, string description) {
        if (locName == null) {
            if (other == null) {
                locName = name;
            }
            else {
                locName = other.locName;
                locDescr = description + " " + locName;
            }
        }

        if (iconSpec == null && other?.iconSpec != null) {
            iconSpec = other.iconSpec;
        }
    }

    public abstract void GetDependencies(IDependencyCollector collector, List<FactorioObject> temp);

    public override string ToString() => name;

    public int CompareTo(FactorioObject? other) => DataUtils.DefaultOrdering.Compare(this, other);

    public bool showInExplorers { get; internal set; } = true;
}

public class FactorioIconPart(string path) {
    public string path = path;
    public float size = 32;
    public float x, y, r = 1, g = 1, b = 1, a = 1;
    public float scale = 1;

    public bool IsSimple() => x == 0 && y == 0 && r == 1 && g == 1 && b == 1 && a == 1 && scale == 1;
}

[Flags]
public enum RecipeFlags {
    UsesMiningProductivity = 1 << 0,
    UsesFluidTemperature = 1 << 2,
    ScaleProductionWithPower = 1 << 3,
    /// <summary>Set when the technology has a research trigger to craft an item</summary>
    HasResearchTriggerCraft = 1 << 4,
    /// <summary>Set when the technology has a research trigger to capture a spawner</summary>
    HasResearchTriggerCaptureEntity = 1 << 5,
    /// <summary>Set when the technology has a research trigger to mine an entity (including a resource)</summary>
    HasResearchTriggerMineEntity = 1 << 6,
    /// <summary>Set when the technology has a research trigger to build an entity</summary>
    HasResearchTriggerBuildEntity = 1 << 7,
    /// <summary>Set when the technology has a research trigger to launch a space platform starter pack</summary>
    HasResearchTriggerCreateSpacePlatform = 1 << 8,

    HasResearchTriggerMask = HasResearchTriggerCraft | HasResearchTriggerCaptureEntity | HasResearchTriggerMineEntity | HasResearchTriggerBuildEntity
        | HasResearchTriggerCreateSpacePlatform,
}

public abstract class RecipeOrTechnology : FactorioObject {
    public EntityCrafter[] crafters { get; internal set; } = null!; // null-forgiving: Initialized by CalculateMaps
    public Ingredient[] ingredients { get; internal set; } = null!; // null-forgiving: Initialized by LoadRecipeData, LoadTechnologyData, and after all calls to CreateSpecialRecipe
    public Product[] products { get; internal set; } = null!; // null-forgiving: Initialized by LoadRecipeData, LoadTechnologyData, and after all calls to CreateSpecialRecipe
    public Entity? sourceEntity { get; internal set; }
    public Goods? mainProduct { get; internal set; }
    public float time { get; internal set; }
    public bool enabled { get; internal set; }
    public bool hidden { get; internal set; }
    public RecipeFlags flags { get; internal set; }
    public override string type => "Recipe";

    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Recipes;

    public override void GetDependencies(IDependencyCollector collector, List<FactorioObject> temp) {
        if (ingredients.Length > 0) {
            temp.Clear();
            foreach (var ingredient in ingredients) {
                if (ingredient.variants != null) {
                    collector.Add(ingredient.variants, DependencyList.Flags.IngredientVariant);
                }
                else {
                    temp.Add(ingredient.goods);
                }
            }
            if (temp.Count > 0) {
                collector.Add(temp, DependencyList.Flags.Ingredient);
            }
        }
        collector.Add(crafters, DependencyList.Flags.CraftingEntity);
        if (sourceEntity != null) {
            collector.Add(new[] { sourceEntity.id }, DependencyList.Flags.SourceEntity);
        }
    }

    public bool CanFit(int itemInputs, int fluidInputs, Goods[]? slots) {
        foreach (var ingredient in ingredients) {
            if (ingredient.goods is Item && --itemInputs < 0) {
                return false;
            }

            if (ingredient.goods is Fluid && --fluidInputs < 0) {
                return false;
            }

            if (slots != null && !slots.Contains(ingredient.goods)) {
                return false;
            }
        }
        return true;
    }

    public bool CanAcceptModule(ObjectWithQuality<Module> module) => CanAcceptModule(module.target);
    public virtual bool CanAcceptModule(Module _) => true;
}

public enum FactorioObjectSpecialType {
    Normal,
    Voiding,
    Barreling,
    Stacking,
    Pressurization,
    Crating,
}

public class Recipe : RecipeOrTechnology {
    public AllowedEffects allowedEffects { get; internal set; }
    public string[]? allowedModuleCategories { get; internal set; }
    public Technology[] technologyUnlock { get; internal set; } = [];
    public Dictionary<Technology, float> technologyProductivity { get; internal set; } = [];
    public bool HasIngredientVariants() {
        foreach (var ingredient in ingredients) {
            if (ingredient.variants != null) {
                return true;
            }
        }

        return false;
    }

    public override void GetDependencies(IDependencyCollector collector, List<FactorioObject> temp) {
        base.GetDependencies(collector, temp);

        if (!enabled) {
            collector.Add(technologyUnlock, DependencyList.Flags.TechnologyUnlock);
        }
    }

    public override bool CanAcceptModule(Module module) => EntityWithModules.CanAcceptModule(module.moduleSpecification, allowedEffects, allowedModuleCategories);
}

public class Mechanics : Recipe {
    internal FactorioObject source { get; set; } = null!; // null-forgiving: Set by CreateSpecialRecipe
    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Mechanics;
    public override string type => "Mechanics";
}

public class Ingredient : IFactorioObjectWrapper {
    public readonly float amount;
    public Goods goods { get; internal set; }
    public Goods[]? variants { get; internal set; }
    public TemperatureRange temperature { get; internal set; } = TemperatureRange.Any;
    public Ingredient(Goods goods, float amount) {
        this.goods = goods;
        this.amount = amount;
        if (goods is Fluid fluid) {
            temperature = fluid.temperatureRange;
        }
    }

    string IFactorioObjectWrapper.text {
        get {
            string text = goods.locName;
            if (amount != 1f) {
                text = amount + "x " + text;
            }

            if (!temperature.IsAny()) {
                text += " (" + temperature + ")";
            }

            return text;
        }
    }

    FactorioObject IFactorioObjectWrapper.target => goods;

    float IFactorioObjectWrapper.amount => amount;

    public bool ContainsVariant(Goods product) {
        if (goods == product) {
            return true;
        }

        if (variants != null) {
            return Array.IndexOf(variants, product) >= 0;
        }

        return false;
    }
}

public class Product : IFactorioObjectWrapper {
    public readonly Goods goods;
    internal readonly float amountMin;
    internal readonly float amountMax;
    internal readonly float probability;
    public readonly float amount; // This is average amount including probability and range
    internal float productivityAmount { get; private set; }

    public void SetCatalyst(float catalyst) {
        float catalyticMin = amountMin - catalyst;
        float catalyticMax = amountMax - catalyst;

        if (catalyticMax <= 0) {
            productivityAmount = 0f;
        }
        else if (catalyticMin >= 0f) {
            productivityAmount = (catalyticMin + catalyticMax) * 0.5f * probability;
        }
        else {
            // TODO super duper rare case, might not be precise
            productivityAmount = probability * catalyticMax * catalyticMax * 0.5f / (catalyticMax - catalyticMin);
        }
    }

    internal float GetAmountForRow(RecipeRow row) => GetAmountPerRecipe(row.parameters.productivity) * (float)row.recipesPerSecond;
    internal float GetAmountPerRecipe(float productivityBonus) => amount + (productivityBonus * productivityAmount);

    public Product(Goods goods, float amount) {
        this.goods = goods;
        amountMin = amountMax = this.amount = productivityAmount = amount;
        probability = 1f;
    }

    public Product(Goods goods, float min, float max, float probability) {
        this.goods = goods;
        amountMin = min;
        amountMax = max;
        this.probability = probability;
        amount = productivityAmount = probability * (min + max) / 2;
    }

    public bool IsSimple => amountMin == amountMax && probability == 1f;

    FactorioObject IFactorioObjectWrapper.target => goods;

    string IFactorioObjectWrapper.text {
        get {
            string text = goods.locName;

            if (amountMin != 1f || amountMax != 1f) {
                text = DataUtils.FormatAmount(amountMax, UnitOfMeasure.None) + "x " + text;

                if (amountMin != amountMax) {
                    text = DataUtils.FormatAmount(amountMin, UnitOfMeasure.None) + "-" + text;
                }
            }
            if (probability != 1f) {
                text = DataUtils.FormatAmount(probability, UnitOfMeasure.Percent) + " " + text;
            }

            return text;
        }
    }
    float IFactorioObjectWrapper.amount => amount;
}

// Abstract base for anything that can be produced or consumed by recipes (etc)
public abstract class Goods : FactorioObject {
    public float fuelValue { get; internal set; }
    public abstract bool isPower { get; }
    public Fluid? fluid => this as Fluid;
    public Recipe[] production { get; internal set; } = [];
    public Recipe[] usages { get; internal set; } = [];
    public FactorioObject[] miscSources { get; internal set; } = [];
    public Entity[] fuelFor { get; internal set; } = [];
    public abstract UnitOfMeasure flowUnitOfMeasure { get; }
    public bool isLinkable { get; internal set; } = true;

    public override void GetDependencies(IDependencyCollector collector, List<FactorioObject> temp) => collector.Add(production.Concat(miscSources).ToArray(), DependencyList.Flags.Source);

    public virtual bool HasSpentFuel([MaybeNullWhen(false)] out Item spent) {
        spent = null;
        return false;
    }
}

public class Item : Goods {
    /// <summary>
    /// The prototypes in this array will be loaded in order, before any other prototypes.
    /// This should correspond to the prototypes for subclasses of item, with more derived classes listed before their base classes.
    /// e.g. If ItemWithHealth and ModuleWithHealth C# classes were added, all prototypes for both classes must be listed here.
    /// Ignoring style restrictions, the prototypes could be listed in any order, provided all ModuleWithHealth prototype(s) are listed before "module".
    /// </summary>
    /// <remarks>This forces modules to be loaded before other items, since deserialization otherwise creates Item objects for all spoil results.
    /// It does not protect against modules that spoil into other modules, but one hopes people won't do that.</remarks>
    internal static string[] ExplicitPrototypeLoadOrder { get; } = ["ammo", "module"];

    public Item? fuelResult { get; internal set; }
    public int stackSize { get; internal set; }
    public Entity? placeResult { get; internal set; }
    public Entity? plantResult { get; internal set; }
    public override bool isPower => false;
    public override string type => "Item";
    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Items;
    public override UnitOfMeasure flowUnitOfMeasure => UnitOfMeasure.ItemPerSecond;

    public override bool HasSpentFuel([NotNullWhen(true)] out Item? spent) {
        spent = fuelResult;
        return spent != null;
    }
}

public class Module : Item {
    public ModuleSpecification moduleSpecification { get; internal set; } = null!; // null-forgiving: Initialized by DeserializeItem.
}

internal class Ammo : Item {
    internal HashSet<string> projectileNames { get; } = [];
    internal HashSet<string>? targetFilter { get; set; }
}

public class Fluid : Goods {
    public override string type => "Fluid";
    public string originalName { get; internal set; } = null!; // name without temperature, null-forgiving: Initialized by DeserializeFluid.
    public float heatCapacity { get; internal set; } = 1e-3f;
    public TemperatureRange temperatureRange { get; internal set; }
    public int temperature { get; internal set; }
    public float heatValue { get; internal set; }
    public List<Fluid>? variants { get; internal set; }
    public override bool isPower => false;
    public override UnitOfMeasure flowUnitOfMeasure => UnitOfMeasure.FluidPerSecond;
    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Fluids;
    internal Fluid Clone() => (Fluid)MemberwiseClone();

    internal void SetTemperature(int temp) {
        temperature = temp;
        heatValue = (temp - temperatureRange.min) * heatCapacity;
    }
}

public class Special : Goods {
    internal string? virtualSignal { get; set; }
    internal bool power;
    internal bool isResearch;
    internal bool isVoid;
    public override bool isPower => power;
    public override string type => isPower ? "Power" : "Special";
    public override UnitOfMeasure flowUnitOfMeasure => isVoid ? UnitOfMeasure.None : isPower ? UnitOfMeasure.Megawatt : UnitOfMeasure.PerSecond;
    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.SpecialGoods;
    public override void GetDependencies(IDependencyCollector collector, List<FactorioObject> temp) {
        if (isResearch) {
            collector.Add(Database.technologies.all.ToArray(), DependencyList.Flags.Source);
        }
        else {
            base.GetDependencies(collector, temp);
        }
    }
}

[Flags]
public enum AllowedEffects {
    Speed = 1 << 0,
    Productivity = 1 << 1,
    Consumption = 1 << 2,
    Pollution = 1 << 3,
    Quality = 1 << 4,

    All = Speed | Productivity | Consumption | Pollution | Quality,
    None = 0
}

public class Tile : FactorioObject {
    public Fluid? Fluid { get; internal set; }

    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Tiles;
    public override string type => "Tile";

    public override void GetDependencies(IDependencyCollector collector, List<FactorioObject> temp) {
    }
}

public class Entity : FactorioObject {
    public Product[] loot { get; internal set; } = [];
    public bool mapGenerated { get; internal set; }
    public float mapGenDensity { get; internal set; }
    public float basePower { get; internal set; }
    public float Power(Quality quality)
        => factorioType is "boiler" or "reactor" or "generator" or "burner-generator" ? quality.ApplyStandardBonus(basePower)
        : factorioType is "beacon" ? basePower * quality.BeaconConsumptionFactor
        : basePower;
    public EntityEnergy energy { get; internal set; } = null!; // TODO: Prove that this is always properly initialized. (Do we need an EntityWithEnergy type?)
    public Item[] itemsToPlace { get; internal set; } = null!; // null-forgiving: This is initialized in CalculateMaps.
    internal FactorioObject[] miscSources { get; set; } = [];
    public int size { get; internal set; }
    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Entities;
    public override string type => "Entity";

    public override void GetDependencies(IDependencyCollector collector, List<FactorioObject> temp) {
        if (energy != null) {
            collector.Add(energy.fuels, DependencyList.Flags.Fuel);
        }

        if (mapGenerated) {
            return;
        }

        collector.Add([.. itemsToPlace, .. miscSources], DependencyList.Flags.ItemToPlace);
    }
}

public abstract class EntityWithModules : Entity {
    public AllowedEffects allowedEffects { get; internal set; } = AllowedEffects.None;
    public string[]? allowedModuleCategories { get; internal set; }
    public int moduleSlots { get; internal set; }

    public static bool CanAcceptModule(ModuleSpecification module, AllowedEffects effects, string[]? allowedModuleCategories) {
        if (module.baseProductivity > 0f && (effects & AllowedEffects.Productivity) == 0) {
            return false;
        }

        if (module.baseConsumption < 0f && (effects & AllowedEffects.Consumption) == 0) {
            return false;
        }

        if (module.basePollution < 0f && (effects & AllowedEffects.Pollution) == 0) {
            return false;
        }

        if (module.baseSpeed > 0f && (effects & AllowedEffects.Speed) == 0) {
            return false;
        }

        if (module.baseQuality > 0f && (effects & AllowedEffects.Quality) == 0) {
            return false;
        }

        if (allowedModuleCategories?.Contains(module.category) == false) {
            return false;
        }

        return true;
    }

    public bool CanAcceptModule<T>(ObjectWithQuality<T> module) where T : Module => CanAcceptModule(module.target.moduleSpecification);
    public bool CanAcceptModule(ModuleSpecification module) => CanAcceptModule(module, allowedEffects, allowedModuleCategories);
}

public class EntityCrafter : EntityWithModules {
    public int itemInputs { get; internal set; }
    public int fluidInputs { get; internal set; } // fluid inputs for recipe, not including power
    public bool hasVectorToPlaceResult { get; internal set; }
    public Goods[]? inputs { get; internal set; }
    public RecipeOrTechnology[] recipes { get; internal set; } = null!; // null-forgiving: Set in the first step of CalculateMaps
    private float _craftingSpeed = 1;
    public float baseCraftingSpeed {
        // The speed of a lab is baseSpeed * (1 + researchSpeedBonus) * Math.Min(0.2, 1 + moduleAndBeaconSpeedBonus)
        get => _craftingSpeed * (1 + (factorioType == "lab" ? Project.current.settings.researchSpeedBonus : 0));
        internal set => _craftingSpeed = value;
    }
    public float CraftingSpeed(Quality quality) => factorioType is "agricultural-tower" or "electric-energy-interface" ? baseCraftingSpeed : quality.ApplyStandardBonus(baseCraftingSpeed);
    public EffectReceiver effectReceiver { get; internal set; } = null!;
}

internal class EntityProjectile : Entity {
    internal HashSet<string> placeEntities { get; } = [];
}

internal class EntitySpawner : Entity {
    internal string? capturedEntityName { get; set; }
}

public sealed class Quality : FactorioObject {
    public static Quality Normal { get; internal set; } = null!;
    /// <summary>
    /// Gets the highest quality that is accessible at the current milestones.
    /// </summary>
    public static Quality MaxAccessible {
        get {
            Quality quality = Normal;
            while (quality.nextQuality?.IsAccessibleWithCurrentMilestones() ?? false) {
                quality = quality.nextQuality;
            }
            return quality;
        }
    }

    public Quality? nextQuality { get; internal set; }
    public int level { get; internal set; }

    public override string type => "Quality";
    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Qualities;

    internal List<Technology> technologyUnlock { get; } = [];
    internal Quality? previousQuality { get; set; }

    public override void GetDependencies(IDependencyCollector collector, List<FactorioObject> temp) {
        collector.Add(technologyUnlock.ToArray(), DependencyList.Flags.TechnologyUnlock);
        if (previousQuality != null) {
            collector.Add([previousQuality], DependencyList.Flags.Source);
        }
        if (level != 0) {
            collector.Add(Database.allModules.Where(m => m.moduleSpecification.baseQuality > 0).ToArray(), DependencyList.Flags.Source);
        }
    }

    // applies the "standard" +30% per level bonus
    internal float ApplyStandardBonus(float baseValue) => baseValue * (1 + .3f * level);
    // applies the +100% per level accumulator capacity bonus
    internal float ApplyAccumulatorCapacityBonus(float baseValue) => baseValue * (1 + level);
    // applies the standard +30% per level bonus, but with the exception that the result is floored to the nearest hundredth
    // Modules use this: a 25% normal bonus is a 32% uncommon bonus, not 32.5%.
    internal float ApplyModuleBonus(float baseValue) => MathF.Floor(ApplyStandardBonus(baseValue) * 100) / 100;
    // applies the .2 per level beacon transmission bonus
    internal float ApplyBeaconBonus(float baseValue) => baseValue + level * .2f;

    public float StandardBonus => .3f * level;
    public float AccumulatorCapacityBonus => level;
    public float BeaconTransmissionBonus => .2f * level;
    public float BeaconConsumptionFactor { get; internal set; }
}

/// <summary>
/// Represents a <see cref="FactorioObject"/> with an attached <see cref="Quality"/> modifier.
/// </summary>
/// <typeparam name="T">The concrete type of the quality-modified object.</typeparam>
public interface IObjectWithQuality<out T> : IFactorioObjectWrapper where T : FactorioObject {
    /// <summary>
    /// Gets the <typeparamref name="T"/> object managed by this instance.
    /// </summary>
    new T target { get; }
    /// <summary>
    /// Gets the <see cref="Quality"/> of the object managed by this instance.
    /// </summary>
    Quality quality { get; }
}

public static class ObjectWithQualityExtensions {
    // This method cannot be declared on the interface.
    public static void Deconstruct<T>(this IObjectWithQuality<T> value, out T obj, out Quality quality) where T : FactorioObject {
        obj = value.target;
        quality = value.quality;
    }
}

/// <summary>
/// Represents a <see cref="FactorioObject"/> with an attached <see cref="Quality"/> modifier.
/// </summary>
/// <typeparam name="T">The concrete type of the quality-modified object.</typeparam>
/// <param name="target">The object to be associated with a quality modifier. If this parameter might be <see langword="null"/>,
/// use the implicit conversion from <see cref="ValueTuple{T1, T2}"/> instead.</param>
/// <param name="quality">The quality for this object.</param>
[Serializable]
public sealed class ObjectWithQuality<T>(T target, Quality quality) : IObjectWithQuality<T>, ICustomJsonDeserializer<ObjectWithQuality<T>> where T : FactorioObject {
    /// <inheritdoc/>
    public T target { get; } = target ?? throw new ArgumentNullException(nameof(target));
    /// <inheritdoc/>
    public Quality quality { get; } = quality ?? throw new ArgumentNullException(nameof(quality));

    /// <summary>
    /// Creates a new <see cref="ObjectWithQuality{T}"/> with the current <see cref="target"/> and the specified <see cref="Quality"/>.
    /// </summary>
    public ObjectWithQuality<T> With(Quality quality) => new(target, quality);

    string IFactorioObjectWrapper.text => ((IFactorioObjectWrapper)target).text;
    FactorioObject IFactorioObjectWrapper.target => target;
    float IFactorioObjectWrapper.amount => ((IFactorioObjectWrapper)target).amount;

    public static implicit operator ObjectWithQuality<T>?((T? entity, Quality quality) value) => value.entity == null ? null : new(value.entity, value.quality);

    /// <inheritdoc/>
    public static bool Deserialize(ref Utf8JsonReader reader, DeserializationContext context, out ObjectWithQuality<T>? result) {
        if (reader.TokenType == JsonTokenType.String) {
            // Read the old `"entity": "Entity.solar-panel"` format.
            if (Database.objectsByTypeName[reader.GetString()!] is not T obj) {
                context.Error($"Could not convert '{reader.GetString()}' to a {typeof(T).Name}.", ErrorSeverity.MinorDataLoss);
                result = null;
                return true;
            }
            result = new(obj, Quality.Normal);
            return true;
        }
        // This is probably the current `"entity": { "target": "Entity.solar-panel", "quality": "Quality.rare" }` format.
        result = default;
        return false;
    }

    // Ensure ObjectWithQuality<X> equals ObjectWithQuality<Y> as long as the properties are equal, regardless of X and Y.
    public override bool Equals(object? obj) => Equals(obj as IObjectWithQuality<FactorioObject>);
    public bool Equals(IObjectWithQuality<FactorioObject>? other) => other is not null && target == other.target && quality == other.quality;
    public override int GetHashCode() => HashCode.Combine(target, quality);

    public static bool operator ==(ObjectWithQuality<T>? left, ObjectWithQuality<T>? right) => left == (IObjectWithQuality<T>?)right;
    public static bool operator ==(ObjectWithQuality<T>? left, IObjectWithQuality<T>? right) => (left is null && right is null) || (left is not null && left.Equals(right));
    public static bool operator ==(IObjectWithQuality<T>? left, ObjectWithQuality<T>? right) => right == left;
    public static bool operator !=(ObjectWithQuality<T>? left, ObjectWithQuality<T>? right) => !(left == right);
    public static bool operator !=(ObjectWithQuality<T>? left, IObjectWithQuality<T>? right) => !(left == right);
    public static bool operator !=(IObjectWithQuality<T>? left, ObjectWithQuality<T>? right) => !(left == right);
}

public class Effect {
    public float consumption { get; internal set; }
    public float speed { get; internal set; }
    public float productivity { get; internal set; }
    public float pollution { get; internal set; }
    public float quality { get; internal set; }
}

public class EffectReceiver {
    public Effect baseEffect { get; internal set; } = null!;
    public bool usesModuleEffects { get; internal set; }
    public bool usesBeaconEffects { get; internal set; }
    public bool usesSurfaceEffects { get; internal set; }
}

public class EntityInserter : Entity {
    public bool isBulkInserter { get; internal set; }
    public float inserterSwingTime { get; internal set; }
}

public class EntityAccumulator : Entity {
    public float baseAccumulatorCapacity { get; internal set; }
    public float AccumulatorCapacity(Quality quality) => quality.ApplyAccumulatorCapacityBonus(baseAccumulatorCapacity);
}

public class EntityBelt : Entity {
    public float beltItemsPerSecond { get; internal set; }
}

public class EntityReactor : EntityCrafter {
    public float reactorNeighborBonus { get; internal set; }
}

public class EntityBeacon : EntityWithModules {
    public float baseBeaconEfficiency { get; internal set; }
    public float BeaconEfficiency(Quality quality) => quality.ApplyBeaconBonus(baseBeaconEfficiency);
    public float[] profile { get; internal set; } = null!;

    public float GetProfile(int numberOfBeacons) {
        if (numberOfBeacons == 0) {
            return 1f;
        }

        return profile[Math.Min(numberOfBeacons, profile.Length) - 1];
    }
}

public class EntityContainer : Entity {
    public int inventorySize { get; internal set; }
    public string? logisticMode { get; set; }
    public int logisticSlotsCount { get; internal set; }
}

public class Technology : RecipeOrTechnology { // Technology is very similar to recipe
    public float count { get; internal set; } // TODO support formula count
    public Technology[] prerequisites { get; internal set; } = [];
    public List<Recipe> unlockRecipes { get; internal set; } = [];
    public Dictionary<Recipe, float> changeRecipeProductivity { get; internal set; } = [];
    internal bool unlocksFluidMining { get; set; }
    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Technologies;
    public override string type => "Technology";
    /// <summary>
    /// If the technology has a trigger that requires entities, they are stored here.
    /// </summary>
    /// <remarks>Lazy-loaded so the database can load and correctly type (eg EntityCrafter, EntitySpawner, etc.) the entities without having to do another pass.</remarks>
    public IReadOnlyList<Entity> triggerEntities => getTriggerEntities.Value;

    /// <summary>
    /// Sets the value used to construct <see cref="triggerEntities"/>.
    /// </summary>
    internal Lazy<IReadOnlyList<Entity>> getTriggerEntities { get; set; } = new Lazy<IReadOnlyList<Entity>>(() => []);

    public override void GetDependencies(IDependencyCollector collector, List<FactorioObject> temp) {
        base.GetDependencies(collector, temp);
        if (prerequisites.Length > 0) {
            collector.Add(prerequisites, DependencyList.Flags.TechnologyPrerequisites);
        }
        if (flags.HasFlag(RecipeFlags.HasResearchTriggerMineEntity)) {
            // If we have a mining mechanic, use that as the source; otherwise just use the entity.
            collector.Add([.. triggerEntities.Select(e => Database.mechanics.all.SingleOrDefault(m => m.source == e) ?? (FactorioObject)e)], DependencyList.Flags.Source);
        }
        if (flags.HasFlag(RecipeFlags.HasResearchTriggerBuildEntity)) {
            collector.Add(triggerEntities, DependencyList.Flags.Source);
        }
        if (flags.HasFlag(RecipeFlags.HasResearchTriggerCreateSpacePlatform)) {
            var items = Database.items.all.Where(i => i.factorioType == "space-platform-starter-pack");
            collector.Add([.. items.Select(i => Database.objectsByTypeName["Mechanics.launch." + i.name])], DependencyList.Flags.Source);
        }

        if (hidden && !enabled) {
            collector.Add(Array.Empty<FactorioId>(), DependencyList.Flags.Hidden);
        }
    }
}

public enum EntityEnergyType {
    Void,
    Electric,
    Heat,
    SolidFuel,
    FluidFuel,
    FluidHeat,
    Labor, // Special energy type for character
}

public class EntityEnergy {
    public EntityEnergyType type { get; internal set; }
    public TemperatureRange workingTemperature { get; internal set; }
    public TemperatureRange acceptedTemperature { get; internal set; } = TemperatureRange.Any;
    public float emissions { get; internal set; }
    public float drain { get; internal set; }
    public float baseFuelConsumptionLimit { get; internal set; } = float.PositiveInfinity;
    public float FuelConsumptionLimit(Quality quality) => quality.ApplyStandardBonus(baseFuelConsumptionLimit);
    public Goods[] fuels { get; internal set; } = [];
    public float effectivity { get; internal set; } = 1f;
}

public class ModuleSpecification {
    public string category { get; internal set; } = null!;
    public float baseConsumption { get; internal set; }
    public float baseSpeed { get; internal set; }
    public float baseProductivity { get; internal set; }
    public float basePollution { get; internal set; }
    public float baseQuality { get; internal set; }
    public float Consumption(Quality quality) => baseConsumption >= 0 ? baseConsumption : quality.ApplyModuleBonus(baseConsumption);
    public float Speed(Quality quality) => baseSpeed <= 0 ? baseSpeed : quality.ApplyModuleBonus(baseSpeed);
    public float Productivity(Quality quality) => baseProductivity <= 0 ? baseProductivity : quality.ApplyModuleBonus(baseProductivity);
    public float Pollution(Quality quality) => basePollution >= 0 ? basePollution : quality.ApplyModuleBonus(basePollution);
    public float Quality(Quality quality) => baseQuality <= 0 ? baseQuality : quality.ApplyModuleBonus(baseQuality);
}

public struct TemperatureRange(int min, int max) {
    public int min = min;
    public int max = max;

    public static readonly TemperatureRange Any = new TemperatureRange(int.MinValue, int.MaxValue);
    public readonly bool IsAny() => min == int.MinValue && max == int.MaxValue;

    public readonly bool IsSingle() => min == max;

    public TemperatureRange(int single) : this(single, single) { }

    public override readonly string ToString() {
        if (min == max) {
            return min + "°";
        }

        return min + "°-" + max + "°";
    }

    public readonly bool Contains(int value) => min <= value && max >= value;
}
