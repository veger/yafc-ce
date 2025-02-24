using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Yafc.UI;

namespace Yafc.Model;

public struct ModuleEffects {
    public float speed;
    public float productivity;
    public float consumption;
    public float quality;

    public readonly float speedMod => MathF.Max(1f + speed, 0.2f);
    public readonly float energyUsageMod => MathF.Max(1f + consumption, 0.2f);
    public readonly float qualityMod => MathF.Max(quality, 0);
    public void AddModules(ObjectWithQuality<Module> module, float count, AllowedEffects allowedEffects = AllowedEffects.All) {
        ModuleSpecification spec = module.target.moduleSpecification;
        Quality quality = module.quality;
        if (allowedEffects.HasFlags(AllowedEffects.Speed)) {
            speed += spec.Speed(quality) * count;
        }

        if (allowedEffects.HasFlags(AllowedEffects.Productivity) && spec.baseProductivity > 0f) {
            productivity += spec.Productivity(quality) * count;
        }

        if (allowedEffects.HasFlags(AllowedEffects.Consumption)) {
            consumption += spec.Consumption(quality) * count;
        }

        if (allowedEffects.HasFlags(AllowedEffects.Quality)) {
            this.quality += spec.Quality(quality) * count;
        }
    }

    public readonly int GetModuleSoftLimit(ObjectWithQuality<Module> module, int hardLimit) {
        ModuleSpecification spec = module.target.moduleSpecification;
        Quality quality = module.quality;

        if (spec.baseProductivity > 0f || spec.baseSpeed > 0f || spec.basePollution < 0f) {
            return hardLimit;
        }

        if (spec.baseConsumption < 0f) {
            return MathUtils.Clamp(MathUtils.Ceil(-(consumption + 0.8f) / spec.Consumption(quality)), 0, hardLimit);
        }

        return 0;
    }
}

/// <summary>
/// One module that is (or will be) applied to a <see cref="RecipeRow"/>, and the number of times it should appear.
/// </summary>
/// <remarks>Immutable. To modify, modify the owning <see cref="ModuleTemplate"/>.</remarks>
public class RecipeRowCustomModule(ModuleTemplate owner, ObjectWithQuality<Module> module, int fixedCount = 0) : ModelObject<ModuleTemplate>(owner) {
    public ObjectWithQuality<Module> module { get; } = module ?? throw new ArgumentNullException(nameof(module));
    public int fixedCount { get; } = fixedCount;
}

/// <summary>
/// The template that determines what modules are (or will be) applied to a <see cref="RecipeRow"/>.
/// </summary>
/// <remarks>Immutable. To modify, call <see cref="GetBuilder"/>, modify the builder, and call <see cref="ModuleTemplateBuilder.Build"/>.</remarks>
[DeserializeWithNonPublicConstructor]
public class ModuleTemplate : ModelObject<ModelObject> {
    /// <summary>
    /// The beacon to use, if any, for the associated <see cref="RecipeRow"/>.
    /// </summary>
    public ObjectWithQuality<EntityBeacon>? beacon { get; }
    /// <summary>
    /// The modules, if any, to directly insert into the crafting entity.
    /// </summary>
    public ReadOnlyCollection<RecipeRowCustomModule> list { get; private set; } = new([]); // Must be a distinct collection object to accommodate the deserializer.
    /// <summary>
    /// The modules, if any, to insert into beacons that affect the crafting entity.
    /// </summary>
    public ReadOnlyCollection<RecipeRowCustomModule> beaconList { get; private set; } = new([]); // Must be a distinct collection object to accommodate the deserializer.

    private ModuleTemplate(ModelObject owner, ObjectWithQuality<EntityBeacon>? beacon) : base(owner) => this.beacon = beacon;

    public bool IsCompatibleWith([NotNullWhen(true)] RecipeRow? row) {
        if (row?.entity == null) {
            return false;
        }

        bool hasFloodfillModules = false;
        bool hasCompatibleFloodfill = false;
        int totalModules = 0;

        foreach (var module in list) {
            bool isCompatibleWithModule = row.recipe.CanAcceptModule(module.module) && row.entity.target.CanAcceptModule(module.module);

            if (module.fixedCount == 0) {
                hasFloodfillModules = true;
                hasCompatibleFloodfill |= isCompatibleWithModule;
            }
            else {
                if (!isCompatibleWithModule) {
                    return false;
                }

                totalModules += module.fixedCount;
            }
        }

        return (!hasFloodfillModules || hasCompatibleFloodfill) && row.entity.target.moduleSlots >= totalModules;
    }

    internal void GetModulesInfo(RecipeRow row, EntityCrafter entity, ref ModuleEffects effects, ref UsedModule used, ModuleFillerParameters? filler) {
        List<(ObjectWithQuality<Module> module, int count, bool beacon)> buffer = [];
        int beaconedModules = 0;
        ObjectWithQuality<Module>? nonBeacon = null;
        used.modules = null;
        int remaining = entity.moduleSlots;

        foreach (var module in list) {
            if (!entity.CanAcceptModule(module.module) || !row.recipe.CanAcceptModule(module.module)) {
                continue;
            }

            if (remaining <= 0) {
                break;
            }

            int count = Math.Min(module.fixedCount == 0 ? int.MaxValue : module.fixedCount, remaining);
            remaining -= count;
            nonBeacon ??= module.module;
            buffer.Add((module.module, count, false));
            effects.AddModules(module.module, count);
        }

        if (beacon != null) {
            int beaconCount = CalculateBeaconCount();
            if (beaconCount > 0) {
                float beaconEfficiency = beacon.GetBeaconEfficiency() * beacon.target.GetProfile(beaconCount);
                foreach (var module in beaconList) {
                    beaconedModules += module.fixedCount;
                    buffer.Add((module.module, module.fixedCount, true));
                    effects.AddModules(module.module, beaconEfficiency * module.fixedCount);
                }

                used.beacon = beacon;
                used.beaconCount = beaconCount;
            }
        }
        else {
            filler?.AutoFillBeacons(row.recipe.target, entity, ref effects, ref used);
        }

        used.modules = [.. buffer];
    }

    public int CalculateBeaconCount() {
        if (beacon is null) {
            throw new InvalidOperationException($"Must not call {nameof(CalculateBeaconCount)} when {nameof(beacon)} is null.");
        }

        int moduleCount = 0;

        foreach (var element in beaconList) {
            moduleCount += element.fixedCount;
        }

        return ((moduleCount - 1) / beacon.target.moduleSlots) + 1;
    }

    /// <summary>
    /// Get a <see cref="ModuleTemplateBuilder"/> initialized to rebuild the contents of this <see cref="ModuleTemplate"/>.
    /// </summary>
    public ModuleTemplateBuilder GetBuilder() => new() {
        beacon = beacon,
        list = [.. list.Select(m => (m.module, m.fixedCount))],
        beaconList = [.. beaconList.Select(m => (m.module, m.fixedCount))]
    };

    internal static ModuleTemplate Build(ModelObject owner, ModuleTemplateBuilder builder) {
#pragma warning disable IDE0017 // False positive: convertList cannot be called before the assignment completes
        ModuleTemplate modules = new(owner, builder.beacon);
#pragma warning restore IDE0017
        modules.list = convertList(builder.list);
        modules.beaconList = convertList(builder.beaconList);

        return modules;

        ReadOnlyCollection<RecipeRowCustomModule> convertList(List<(ObjectWithQuality<Module> module, int fixedCount)> list)
            => list.Select(m => new RecipeRowCustomModule(modules, m.module, m.fixedCount)).ToList().AsReadOnly();
    }
}

/// <summary>
/// An object that can be used to configure and build a <see cref="ModuleTemplate"/>.
/// </summary>
public class ModuleTemplateBuilder {
    /// <summary>
    /// The beacon to be stored in <see cref="ModuleTemplate.beacon"/> after building.
    /// </summary>
    public ObjectWithQuality<EntityBeacon>? beacon { get; set; }
    /// <summary>
    /// The list of <see cref="Module"/>s and counts to be stored in <see cref="ModuleTemplate.list"/> after building.
    /// </summary>
    public List<(ObjectWithQuality<Module> module, int fixedCount)> list { get; set; } = [];
    /// <summary>
    /// The list of <see cref="Module"/>s and counts to be stored in <see cref="ModuleTemplate.beaconList"/> after building.
    /// </summary>
    public List<(ObjectWithQuality<Module> module, int fixedCount)> beaconList { get; set; } = [];

    /// <summary>
    /// Builds a <see cref="ModuleTemplate"/> from this <see cref="ModuleTemplateBuilder"/>.
    /// </summary>
    /// <param name="owner">The <see cref="RecipeRow"/> or <see cref="ProjectModuleTemplate"/> that will own the built <see cref="ModuleTemplate"/>.</param>
    public ModuleTemplate Build(ModelObject owner) => ModuleTemplate.Build(owner, this);
}

// Stores collection on ProductionLink recipe was linked to the previous computation
internal class RecipeLinks {
    public required ObjectWithQuality<Goods>[] ingredientGoods;
    public required ProductionLink?[] ingredients;
    public ProductionQualityLink products = new();
    public ProductionLink? fuel;
    public ProductionLink? spentFuel;
}

/// <summary>
/// Stores the production links for each pair of (a) index into <see cref="RecipeOrTechnology.products"/> and (b) quality level.
/// The index is the same value as the index into the old <c><see cref="ProductionLink"/>?[]</c> field.
/// </summary>
internal sealed class ProductionQualityLink {
    private readonly Dictionary<(int, Quality), ProductionLink?> _links = [];
    public ProductionLink? this[int idx, Quality quality] {
        get {
            _ = _links.TryGetValue((idx, quality), out ProductionLink? link);
            return link;
        }
        set => _links[(idx, quality)] = value;
    }

    public IEnumerator<ProductionLink?> GetEnumerator() => _links.Values.GetEnumerator();
}

public interface IElementGroup<TElement> {
    List<TElement> elements { get; }
    bool expanded { get; set; }
}

public interface IGroupedElement<TGroup> {
    void SetOwner(TGroup newOwner);
    TGroup? subgroup { get; }
    bool visible { get; }
}

public class RecipeRow : ModelObject<ProductionTable>, IGroupedElement<ProductionTable> {
    private ObjectWithQuality<EntityCrafter>? _entity;
    private ObjectWithQuality<Goods>? _fuel;
    private float _fixedBuildings;
    private ObjectWithQuality<Goods>? _fixedProduct;
    private ModuleTemplate? _modules;

    public ObjectWithQuality<RecipeOrTechnology> recipe { get; }
    // Variable parameters
    public ObjectWithQuality<EntityCrafter>? entity {
        get => _entity;
        set {
            if (_entity == value) {
                // Nothing to do
                return;
            }

            if (SerializationMap.IsDeserializing) {
                // Just apply the deserialized entity and stop further processing
                _entity = value;
                return;
            }

            // By default show the total item consumption and production signals for unloading crafters (miners, recyclers, etc) when they output multiple products.
            // Don't turn the setting on when just changing the quality, though.
            showTotalIO |= (value?.target != _entity?.target) && value?.target.hasVectorToPlaceResult == true && recipe.target.products.Length > 1;

            if (fixedBuildings == 0 || (fixedFuel && !(value?.target.energy.fuels ?? []).Contains(_fuel?.target))) {
                // We're either changing both the entity and the fuel (changing between electric, fluid-burning, item-burning, heat-powered, and steam-powered crafter categories),
                // or fixedBuilding is zero.
                // Don't try to preserve fuel consumption in these cases.
                fixedBuildings = 0;
                _entity = value;
            }
            else {
                using (new ChangeModulesOrEntity(this)) {
                    _entity = value;
                }
            }
        }
    }
    public ObjectWithQuality<Goods>? fuel {
        get => _fuel;
        set {
            if (SerializationMap.IsDeserializing || fixedBuildings == 0 || _fuel == value) {
                _fuel = value;
            }
            else if (fixedProduct != null && (fuel.FuelResult() == fixedProduct || value.FuelResult() == fixedProduct)) {
                if (Products.SingleOrDefault(p => p.Goods == fixedProduct, false) is not RecipeRowProduct product) {
                    fixedBuildings = 0; // We couldn't find the Product corresponding to fixedProduct. Just clear the fixed amount.
                    _fuel = value;
                }
                else {
                    // We're changing the fuel and at least one of the current or new fuel burns to the fixed product
                    float oldAmount = product.Amount;

                    _fuel = value;
                    parameters = RecipeParameters.CalculateParameters(this);
                    float newAmount = Products.First(p => p.Goods == fixedProduct).Amount;

                    fixedBuildings *= oldAmount / newAmount;
                }
            }
            else if (fixedFuel) {
                if (value == null || _fuel == null || _fuel.target.fuelValue == 0) {
                    fixedBuildings = 0;
                }
                else {
                    fixedBuildings *= value.target.fuelValue / _fuel.target.fuelValue;
                }
                _fuel = value;
            }
            else {
                _fuel = value;
            }
        }
    }
    internal RecipeLinks links { get; set; }
    /// <summary>
    /// If not zero, the fixed building count entered by the user, or the number of buildings required to generate the specified fixed consumption/production.
    /// Read <see cref="fixedFuel"/>, <see cref="fixedIngredient"/>, and <see cref="fixedProduct"/> to determine which value was fixed in the UI.
    /// This property is set/modified so the solver gets the correct answer without testing the values of those properties.
    /// </summary>
    public float fixedBuildings {
        get => _fixedBuildings;
        set {
            _fixedBuildings = value;
            if (value == 0) {
                fixedFuel = false;
                fixedIngredient = null;
                fixedProduct = null;
            }
        }
    }
    /// <summary>
    /// If <see langword="true"/>, <see cref="fixedBuildings"/> is set to control the fuel consumption.
    /// </summary>
    public bool fixedFuel { get; set; }
    /// <summary>
    /// If not <see langword="null"/>, <see cref="fixedBuildings"/> is set to control the consumption of this ingredient.
    /// </summary>
    public ObjectWithQuality<Goods>? fixedIngredient { get; set; }
    /// <summary>
    /// If not <see langword="null"/>, <see cref="fixedBuildings"/> is set to control the production of this product.
    /// </summary>
    public ObjectWithQuality<Goods>? fixedProduct {
        get => _fixedProduct;
        set {
            if (value == null) {
                _fixedProduct = null;
            }
            else {
                // This takes advantage of the fact that ObjectWithQuality automatically downgrades high-quality fluids (etc.) to normal.
                var products = recipe.target.products.AsEnumerable().Select(p => new ObjectWithQuality<Goods>(p.goods, recipe.quality));
                if (value != Database.itemOutput && products.All(p => p != value)) {
                    // The UI doesn't know the difference between a product and a spent fuel, but we care about the difference
                    _fixedProduct = null;
                    fixedFuel = true;
                }
                else {
                    _fixedProduct = value;
                }
            }
        }
    }
    public int? builtBuildings { get; set; }
    /// <summary>
    /// If <see langword="true"/>, the total item consumption and production signals are shown.
    /// </summary>
    public bool showTotalIO { get; set; } = false;
    /// <summary>
    /// If <see langword="true"/>, the enabled checkbox for this recipe is checked.
    /// </summary>
    public bool enabled { get; set; } = true;
    /// <summary>
    /// If <see langword="true"/>, the enabled checkboxes for this recipe and all its parent recipes are checked.
    /// If <see langword="false"/>, at least one enabled checkbox for this recipe or its ancestors is unchecked.
    /// </summary>
    public bool hierarchyEnabled { get; internal set; }
    public int tag { get; set; }

    public RowHighlighting highlighting =>
        tag switch {
            1 => RowHighlighting.Green,
            2 => RowHighlighting.Yellow,
            3 => RowHighlighting.Red,
            4 => RowHighlighting.Blue,
            _ => RowHighlighting.None
        };

    [Obsolete("Deprecated", true)]
    public Module module {
        set {
            if (value != null) {
                modules = new ModuleTemplateBuilder { list = { (new(value, Quality.Normal), 0) } }.Build(this);
            }
        }
    }

    public ModuleTemplate? modules {
        get => _modules;
        set {
            if (SerializationMap.IsDeserializing || fixedBuildings == 0 || _modules == value) {
                _modules = value;
            }
            else {
                using (new ChangeModulesOrEntity(this)) {
                    _modules = value;
                }
            }
        }
    }

    public ProductionTable? subgroup { get; set; }
    public HashSet<FactorioObject> variants { get; } = [];
    [SkipSerialization] public ProductionTable linkRoot => subgroup ?? owner;

    public RecipeRowIngredient FuelInformation => new(fuel, fuelUsagePerSecond, links.fuel, (fuel?.target as Fluid)?.variants?.ToArray());
    public IEnumerable<RecipeRowIngredient> Ingredients {
        get {
            return hierarchyEnabled ? @internal() : Enumerable.Repeat<RecipeRowIngredient>((null, 0, null, null), recipe.target.ingredients.Length);

            IEnumerable<RecipeRowIngredient> @internal() {
                for (int i = 0; i < recipe.target.ingredients.Length; i++) {
                    Ingredient ingredient = recipe.target.ingredients[i];
                    yield return (links.ingredientGoods[i], ingredient.amount * (float)recipesPerSecond, links.ingredients[i], ingredient.variants);
                }
            }
        }
    }

    public IEnumerable<RecipeRowProduct> Products => BuildProducts(false);
    public IEnumerable<RecipeRowProduct> ProductsForSolver => BuildProducts(true);

    private IEnumerable<RecipeRowProduct> BuildProducts(bool forSolver) {
        float factor = forSolver ? 1 : (float)recipesPerSecond; // recipesPerSecond can be 0 when running the solver, which would create useless results.
        IObjectWithQuality<Item>? spentFuel = fuel.FuelResult();
        bool handledFuel = spentFuel == null || forSolver; // If we're running the solver or there's no spent fuel, it's already handled.

        List<float> upgradeProbabilities = [1];
        if (parameters.activeEffects.qualityMod > 0) {
            Quality? quality = recipe.quality;
            float runningProbability = 1;
            // Fill upgradeProbabilities with "this quality or better" probabilities.
            while (quality != null && quality < Quality.MaxAccessible) {
                runningProbability *= quality.UpgradeChance;
                upgradeProbabilities.Add(Math.Min(1, runningProbability * parameters.activeEffects.qualityMod));
                quality = quality.nextQuality;
            }
            // Subtract the "next quality or better" probabilities to get just the "exactly this quality" probability.
            for (int j = 0; j < upgradeProbabilities.Count - 1; j++) {
                upgradeProbabilities[j] -= upgradeProbabilities[j + 1];
            }
        }

        for (int i = 0; i < recipe.target.products.Length; i++) {
            Product product = recipe.target.products[i];

            if (hierarchyEnabled) {
                Quality quality = recipe.quality;
                float baseAmount = product.GetAmountPerRecipe(parameters.productivity);
                if (product.goods is Fluid || product.goods == Database.science.target) {
                    // Upgrade chances don't affect fluids or science
                    yield return (product.goods.With(Quality.Normal), baseAmount * factor, links.products[i, Quality.Normal], product.percentSpoiled);
                }
                else {
                    for (int j = 0; j < upgradeProbabilities.Count; j++) {
                        // The result amount for this quality is the normal output amount times the probability of this quality,
                        float amount = baseAmount * upgradeProbabilities[j];
                        if (!handledFuel && product.goods.With(quality) == spentFuel) {
                            // ... plus the spent fuel, if applicable.
                            amount += parameters.fuelUsagePerSecondPerRecipe;
                            handledFuel = true;
                        }

                        if (amount > 0) {
                            yield return (product.goods.With(quality), amount * factor, links.products[i, quality], product.percentSpoiled);
                        }
                        quality = quality.nextQuality!;
                    }
                }
            }
            else {
                yield return (null, 0, null, null);
            }
        }

        if (!handledFuel) {
            yield return hierarchyEnabled ? ((spentFuel?.target, spentFuel?.quality!), fuelUsagePerSecond, links.spentFuel, null) : (null, 0, null, null);
        }
    }

    private SetKeyboardFocus _focusBuiltCount, _focusFixedCount;

    /// <summary>
    /// Returns <see cref="SetKeyboardFocus.Always"/> exactly once after each call to <see cref="FocusBuiltCountOnNextDraw"/>, to focus the newly created edit box on that draw cycle.
    /// </summary>
    public SetKeyboardFocus ShouldFocusBuiltCountThisTime() {
        SetKeyboardFocus result = _focusBuiltCount;
        _focusBuiltCount = SetKeyboardFocus.No;
        return result;
    }
    /// <summary>
    /// Call when preparing to add a built building count edit box, so the new box will be focused as part of the next draw loop.
    /// </summary>
    public void FocusBuiltCountOnNextDraw() => _focusBuiltCount = SetKeyboardFocus.Always;

    /// <summary>
    /// Returns <see cref="SetKeyboardFocus.Always"/> exactly once after each call to <see cref="FocusFixedCountOnNextDraw"/>, to focus the newly created edit box on that draw cycle.
    /// </summary>
    public SetKeyboardFocus ShouldFocusFixedCountThisTime() {
        SetKeyboardFocus result = _focusFixedCount;
        _focusFixedCount = SetKeyboardFocus.No;
        return result;
    }
    /// <summary>
    /// Call when preparing to add or move a fixed count edit box (building, fuel, ingredient, or product), so the new box will be focused as part of the next draw loop.
    /// </summary>
    public void FocusFixedCountOnNextDraw() => _focusFixedCount = SetKeyboardFocus.Always;


    // Computed variables
    internal RecipeParameters parameters { get; set; } = RecipeParameters.Empty;
    public double recipesPerSecond { get; internal set; }
    internal float fuelUsagePerSecond => (float)(parameters.fuelUsagePerSecondPerRecipe * recipesPerSecond);
    public UsedModule usedModules => parameters.modules;
    public WarningFlags warningFlags => parameters.warningFlags;
    public bool FindLink(IObjectWithQuality<Goods> goods, [MaybeNullWhen(false)] out ProductionLink link) => linkRoot.FindLink(goods, out link);

    public T GetVariant<T>(T[] options) where T : FactorioObject {
        foreach (var option in options) {
            if (variants.Contains(option)) {
                return option;
            }
        }

        return options[0];
    }

    public void ChangeVariant<T>(T was, T now) where T : FactorioObject {
        _ = variants.Remove(was);
        _ = variants.Add(now);
    }

    [MemberNotNullWhen(true, nameof(subgroup))]
    public bool isOverviewMode => subgroup != null && !subgroup.expanded;
    public float buildingCount => (float)recipesPerSecond * parameters.recipeTime;
    public bool visible { get; internal set; } = true;

    public RecipeRow(ProductionTable owner, ObjectWithQuality<RecipeOrTechnology> recipe) : base(owner) {
        this.recipe = recipe ?? throw new ArgumentNullException(nameof(recipe), "Recipe does not exist");

        links = new RecipeLinks {
            ingredients = new ProductionLink[recipe.target.ingredients.Length],
            ingredientGoods = new ObjectWithQuality<Goods>[recipe.target.ingredients.Length],
        };
    }

    protected internal override void ThisChanged(bool visualOnly) => owner.ThisChanged(visualOnly);

    public void SetOwner(ProductionTable parent) => owner = parent;

    public void RemoveFixedModules() {
        if (modules == null) {
            return;
        }

        CreateUndoSnapshot();
        modules = null;
    }
    public void SetFixedModule(ObjectWithQuality<Module> module) {
        ModuleTemplateBuilder builder = modules?.GetBuilder() ?? new();
        builder.list = [(module, 0)];
        this.RecordUndo().modules = builder.Build(this);
    }

    public ModuleFillerParameters? GetModuleFiller() {
        var table = linkRoot;
        while (table != null) {
            if (table.modules != null) {
                return table.modules;
            }

            table = (table.owner as RecipeRow)?.owner;
        }

        return null;
    }

    internal void GetModulesInfo((float recipeTime, float fuelUsagePerSecondPerBuilding) recipeParams, EntityCrafter entity, ref ModuleEffects effects, ref UsedModule used) {
        ModuleFillerParameters? filler = null;
        var useModules = modules;
        if (useModules == null || useModules.beacon == null) {
            filler = GetModuleFiller();
        }

        if (useModules == null) {
            filler?.GetModulesInfo(recipeParams, this, entity, ref effects, ref used);
        }
        else {
            useModules.GetModulesInfo(this, entity, ref effects, ref used, filler);
        }
    }

    /// <summary>
    /// Call to inform this <see cref="RecipeRow"/> that the applicable <see cref="ModuleFillerParameters"/> are about to change.
    /// </summary>
    /// <returns>If not <see langword="null"/>, an <see cref="Action"/> to perform after the change has completed
    /// that will update <see cref="fixedBuildings"/> to account for the new modules.</returns>
    internal Action? ModuleFillerParametersChanging() {
        if (fixedFuel || fixedIngredient != null || fixedProduct != null) {
            return new ChangeModulesOrEntity(this).Dispose;
        }
        return null;
    }

    /// <summary>
    /// Handle the <see cref="fixedBuildings"/> changes needed when changing modules or entity, including any bonus work required when the fixed product is also a spent fuel.
    /// </summary>
    private class ChangeModulesOrEntity : IDisposable {
        private readonly RecipeRow row;
        private readonly ObjectWithQuality<Goods>? oldFuel;
        private readonly RecipeParameters oldParameters;

        public ChangeModulesOrEntity(RecipeRow row) {
            this.row = row;
            _ = row.RecordUndo(); // Unnecessary (but not harmful) when called by set_modules or set_entity. Required when called by ModuleFillerParametersChanging.

            // Changing the modules or entity requires up to four steps:
            // (1) Change the fuel to void (boosting fixedBuildings in RecipeRow.set_fuel to account for lost fuel consumption)
            // (2) Change the actual target value (in the caller)
            // (3) Update fixedBuildings to account for the value the caller intended to change (in Dispose)
            // (4) Restore the fuel (row.fuel = oldFuel in Dispose, reducing fixedBuildings in RecipeRow.set_fuel to account for regained fuel consumption)
            // Step 1 is only performed when the fixed product is the sum of spent fuel and recipe production.
            // Step 4 is only performed after step 1 and when the possibly-changed entity accepts the old fuel.
            //      If the entity does not accept the old fuel, a caller must set an appropriate fuel.

            if (row.fixedProduct != null && row.fixedProduct == row.fuel.FuelResult()) {
                oldFuel = row.fuel;
                row.fuel = Database.voidEnergy; // step 1
            }
            // Store the current state of the target RecipeRow for the calculations in Dispose
            oldParameters = RecipeParameters.CalculateParameters(row);
        }

        public void Dispose() {
            row.parameters = RecipeParameters.CalculateParameters(row);

            if (row.fixedFuel) {
                row.fixedBuildings *= oldParameters.fuelUsagePerSecondPerBuilding / row.parameters.fuelUsagePerSecondPerBuilding; // step 3, for fixed fuels
            }
            else if (row.fixedIngredient != null) {
                row.fixedBuildings *= row.parameters.recipeTime / oldParameters.recipeTime; // step 3, for fixed ingredient consumption
            }
            else if (row.fixedProduct != null) {
                if (row.fixedProduct == Database.itemOutput) {
                    float oldAmount = row.recipe.target.products.Where(p => p.goods is Item).Sum(p => p.GetAmountPerRecipe(oldParameters.productivity)) / oldParameters.recipeTime;
                    float newAmount = row.recipe.target.products.Where(p => p.goods is Item).Sum(p => p.GetAmountPerRecipe(row.parameters.productivity)) / row.parameters.recipeTime;
                    row.fixedBuildings *= oldAmount / newAmount; // step 3, for fixed combined production amount
                }
                else if (row.recipe.target.products.SingleOrDefault(p => p.goods == row.fixedProduct.target, false) is not Product product) {
                    row.fixedBuildings = 0; // We couldn't find the Product corresponding to fixedProduct. Just clear the fixed amount.
                }
                else {
                    float oldAmount = product.GetAmountPerRecipe(oldParameters.productivity) / oldParameters.recipeTime;
                    float newAmount = product.GetAmountPerRecipe(row.parameters.productivity) / row.parameters.recipeTime;
                    row.fixedBuildings *= oldAmount / newAmount; // step 3, for fixed individual production amount
                }
            }

            if (oldFuel != null && (row._entity?.target.energy.fuels ?? []).Contains(oldFuel.target)) {
                row.fuel = oldFuel; // step 4
            }
        }
    }
}

public enum RowHighlighting {
    None,
    Green,
    Yellow,
    Red,
    Blue
}

public enum LinkAlgorithm {
    Match,
    AllowOverProduction,
    AllowOverConsumption,
}

/// <summary>
/// A Link is goods whose production and consumption is attempted to be balanced by YAFC across the sheet.
/// </summary>
public class ProductionLink(ProductionTable group, ObjectWithQuality<Goods> goods) : ModelObject<ProductionTable>(group) {
    [Flags]
    public enum Flags {
        // This enum uses powers of two to represent its state.
        // 1 << 0 = 1. 1 << 1 = 2. 1 << 2 = 4. 1 << 3 = 8. An so on.
        // That allows to check Flags state with bit operations. Like a | b.
        // That is a legit and conventional way to do that.
        // https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/enum#designing-flag-enums

        LinkNotMatched = 1 << 0,
        /// <summary>
        /// Indicates if there is a feedback loop that could not get balanced.
        /// It doesn't mean that this link is the problem, but it's a part of the loop.
        /// </summary>
        LinkRecursiveNotMatched = 1 << 1,
        HasConsumption = 1 << 2,
        HasProduction = 1 << 3,
        /// <summary>
        /// The links of a nested table have unmatched production/consumption. These unmatched products are not captured by this link.
        /// </summary>
        /// <remarks> This info was taken from ProductionTableView#dropDownContent.</remarks>
        ChildNotMatched = 1 << 4,
        HasProductionAndConsumption = HasProduction | HasConsumption,
    }

    public ObjectWithQuality<Goods> goods { get; } = goods ?? throw new ArgumentNullException(nameof(goods), "Linked product does not exist");
    public float amount { get; set; }
    public LinkAlgorithm algorithm { get; set; }
    public UnitOfMeasure flowUnitOfMeasure => goods.target.flowUnitOfMeasure;

    // computed variables
    public Flags flags { get; internal set; }
    /// <summary>
    /// Probably the total production of the goods in the link. TODO: Needs to be investigated if it is indeed so.
    /// </summary>
    public float linkFlow { get; internal set; }
    public float notMatchedFlow { get; internal set; }
    /// <summary>
    /// List of recipes belonging to this production link
    /// </summary>
    [SkipSerialization] public HashSet<RecipeRow> capturedRecipes { get; } = [];
    internal int solverIndex;
    public float dualValue { get; internal set; }

    public IEnumerable<string> LinkWarnings {
        get {
            if (!flags.HasFlags(Flags.HasProduction)) {
                yield return "This link has no production (Link ignored)";
            }

            if (!flags.HasFlags(Flags.HasConsumption)) {
                yield return "This link has no consumption (Link ignored)";
            }

            if (flags.HasFlags(Flags.ChildNotMatched)) {
                yield return "Nested table link has unmatched production/consumption. These unmatched products are not captured by this link.";
            }

            if (!flags.HasFlags(Flags.HasProductionAndConsumption) && owner.owner is RecipeRow recipeRow && recipeRow.FindLink(goods, out _)) {
                yield return "Nested tables have their own set of links that DON'T connect to parent links. To connect this product to the outside, remove this link.";
            }

            if (flags.HasFlags(Flags.LinkRecursiveNotMatched)) {
                if (notMatchedFlow <= 0f) {
                    yield return "YAFC was unable to satisfy this link (Negative feedback loop). This doesn't mean that this link is the problem, but it is part of the loop.";
                }
                else {
                    yield return "YAFC was unable to satisfy this link (Overproduction). You can allow overproduction for this link to solve the error.";
                }
            }
        }
    }
}

public record RecipeRowIngredient(ObjectWithQuality<Goods>? Goods, float Amount, ProductionLink? Link, Goods[]? Variants) {
    public static implicit operator (ObjectWithQuality<Goods>? Goods, float Amount, ProductionLink? Link, Goods[]? Variants)(RecipeRowIngredient value)
        => (value.Goods, value.Amount, value.Link, value.Variants);
    public static implicit operator RecipeRowIngredient((ObjectWithQuality<Goods>? Goods, float Amount, ProductionLink? Link, Goods[]? Variants) value)
        => new(value.Goods, value.Amount, value.Link, value.Variants);
}

public record RecipeRowProduct(ObjectWithQuality<Goods>? Goods, float Amount, ProductionLink? Link, float? PercentSpoiled) {
    public static implicit operator (ObjectWithQuality<Goods>? Goods, float Amount, ProductionLink? Link, float? PercentSpoiled)(RecipeRowProduct value)
        => (value.Goods, value.Amount, value.Link, value.PercentSpoiled);
    public static implicit operator RecipeRowProduct((ObjectWithQuality<Goods>? Goods, float Amount, ProductionLink? Link, float? PercentSpoiled) value)
        => new(value.Goods, value.Amount, value.Link, value.PercentSpoiled);
}
