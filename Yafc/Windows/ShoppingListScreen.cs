using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Yafc.Blueprints;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

public class ShoppingListScreen : PseudoScreen {
    private enum DisplayState { Total, Built, Missing }

    private static readonly ObjectSelectOptions<Entity> options
        = new(null, MaxCount: 15, ExtraText: e => DataUtils.FormatAmount(e.heatingPower, UnitOfMeasure.Megawatt));

    private readonly VirtualScrollList<(IObjectWithQuality<FactorioObject>, float)> list;
    private float shoppingCost, totalBuildings, totalModules, totalHeat;
    private List<Entity> pipes = null!, belts = null!, other = null!; //null-forgiving: set by RebuildData
    private bool decomposed = false;
    private static DisplayState displayState {
        get => (DisplayState)(Preferences.Instance.shoppingDisplayState >> 1);
        set {
            Preferences.Instance.shoppingDisplayState = ((int)value) << 1 | (Preferences.Instance.shoppingDisplayState & 1);
            Preferences.Instance.Save();
        }
    }
    private static bool assumeAdequate {
        get => (Preferences.Instance.shoppingDisplayState & 1) != 0;
        set {
            Preferences.Instance.shoppingDisplayState = (Preferences.Instance.shoppingDisplayState & ~1) | (value ? 1 : 0);
            Preferences.Instance.Save();
        }
    }

    private readonly List<RecipeRow> recipes;

    private ShoppingListScreen(List<RecipeRow> recipes) : base(42) {
        list = new(30f, new Vector2(float.PositiveInfinity, 2), ElementDrawer);
        this.recipes = recipes;
        RebuildData();
    }

    private void ElementDrawer(ImGui gui, (IObjectWithQuality<FactorioObject> obj, float count) element, int index) {
        using (gui.EnterRow()) {
            gui.BuildFactorioObjectIcon(element.obj, new IconDisplayStyle(2, MilestoneDisplay.Contained, false));
            gui.RemainingRow().BuildText("x" + DataUtils.FormatAmount(element.count, UnitOfMeasure.None) + ": " + element.obj.target.locName);
        }
        _ = gui.BuildFactorioObjectButtonBackground(gui.lastRect, element.obj);
    }

    public static void Show(List<RecipeRow> recipes) => _ = MainScreen.Instance.ShowPseudoScreen(new ShoppingListScreen(recipes));

    private void RebuildData() {
        decomposed = false;

        // Count buildings and modules
        totalHeat = 0;
        Dictionary<IObjectWithQuality<FactorioObject>, int> counts = [];
        foreach (RecipeRow recipe in recipes) {
            if (recipe.entity != null) {
                IObjectWithQuality<FactorioObject> shopItem = (recipe.entity.target.itemsToPlace?.FirstOrDefault() ?? (FactorioObject)recipe.entity.target).With(recipe.entity.quality);
                _ = counts.TryGetValue(shopItem, out int prev);
                int builtCount = recipe.builtBuildings ?? (assumeAdequate ? MathUtils.Ceil(recipe.buildingCount) : 0);
                int displayCount = displayState switch {
                    DisplayState.Total => MathUtils.Ceil(recipe.buildingCount),
                    DisplayState.Built => builtCount,
                    DisplayState.Missing => MathUtils.Ceil(Math.Max(recipe.buildingCount - builtCount, 0)),
                    _ => throw new InvalidOperationException(nameof(displayState) + " has an unrecognized value.")
                };
                totalHeat += recipe.entity.target.heatingPower * displayCount;
                counts[shopItem] = prev + displayCount;
                if (recipe.usedModules.modules != null) {
                    foreach ((IObjectWithQuality<Module> module, int moduleCount, bool beacon) in recipe.usedModules.modules) {
                        if (!beacon) {
                            _ = counts.TryGetValue(module, out prev);
                            counts[module] = prev + displayCount * moduleCount;
                        }
                    }
                }
            }
        }
        list.data = [.. counts.Where(x => x.Value > 0).Select(x => (x.Key, Value: (float)x.Value)).OrderByDescending(x => x.Value)];

        // Summarize building requirements
        float cost = 0f, buildings = 0f, modules = 0f;
        decomposed = false;
        foreach ((IObjectWithQuality<FactorioObject> obj, float count) in list.data) {
            if (obj.target is Module module) {
                modules += count;
            }
            else if (obj.target is Entity or Item) {
                buildings += count;
            }
            cost += obj.target.Cost() * count;
        }
        shoppingCost = cost;
        totalBuildings = buildings;
        totalModules = modules;

        pipes = Database.entities.all.Where(e => e.factorioType is "pipe" or "pipe-to-ground" or "storage-tank" or "pump" && e.heatingPower > 0)
            .ToList();
        belts = Database.entities.all
            .Where(e => e.factorioType is "transport-belt" or "underground-belt" or "splitter" or "loader" or "loader-1x1" && e.heatingPower > 0)
            .ToList();
        other = Database.entities.all
            .Except(Database.allInserters).Except(pipes).Except(belts)
            .Where(e => e is not EntityCrafter && e.heatingPower > 0)
            .ToList();
    }

    private static readonly (string, string?)[] displayStateOptions = [
        ("Total buildings", "Display the total number of buildings required, ignoring the built building count."),
        ("Built buildings", "Display the number of buildings that are reported in built building count."),
        ("Missing buildings", "Display the number of additional buildings that need to be built.")];
    private static readonly (string, string?)[] assumeAdequateOptions = [
        ("No buildings", "When the built building count is not specified, behave as if it was set to 0."),
        ("Enough buildings", "When the built building count is not specified, behave as if it matches the required building count.")];

    public override void Build(ImGui gui) {
        BuildHeader(gui, "Shopping list");
        gui.BuildText(
            "Total cost of all objects: ¥" + DataUtils.FormatAmount(shoppingCost, UnitOfMeasure.None) + ", buildings: " +
            DataUtils.FormatAmount(totalBuildings, UnitOfMeasure.None) + ", modules: " + DataUtils.FormatAmount(totalModules, UnitOfMeasure.None), TextBlockDisplayStyle.Centered);
        using (gui.EnterRow()) {
            if (gui.BuildRadioGroup(displayStateOptions, (int)displayState, out int newSelected)) {
                displayState = (DisplayState)newSelected;
                RebuildData();
            }
        }
        using (gui.EnterRow()) {
            SchemeColor textColor = displayState == DisplayState.Total ? SchemeColor.PrimaryTextFaint : SchemeColor.PrimaryText;
            gui.BuildText("When not specified, assume:", TextBlockDisplayStyle.Default(textColor), topOffset: .15f);
            if (gui.BuildRadioGroup(assumeAdequateOptions, assumeAdequate ? 1 : 0, out int newSelected, enabled: displayState != DisplayState.Total)) {
                assumeAdequate = newSelected == 1;
                RebuildData();
            }
        }
        gui.AllocateSpacing(1f);
        list.Build(gui);

        if (totalHeat > 0) {
            using (gui.EnterRow(0)) {
                gui.AllocateRect(0, 1.5f);
                gui.BuildText("These entities require " + DataUtils.FormatAmount(totalHeat, UnitOfMeasure.Megawatt));
                gui.BuildFactorioObjectIcon(Database.heat, IconDisplayStyle.Default with { Size = 1.5f });
                gui.BuildText("heat on cold planets.");
            }

            using (gui.EnterRow(0)) {
                gui.BuildText("Allow additional heat for ");
                if (gui.BuildLink("inserters")) {
                    gui.BuildObjectSelectDropDown(Database.allInserters, _ => { }, options);
                }
                gui.BuildText(", ");
                if (gui.BuildLink("pipes")) {
                    gui.BuildObjectSelectDropDown(pipes, _ => { }, options);
                }
                gui.BuildText(", ");
                if (gui.BuildLink("belts")) {
                    gui.BuildObjectSelectDropDown(belts, _ => { }, options);
                }
                gui.BuildText(", and ");
                if (gui.BuildLink("other entities")) {
                    gui.BuildObjectSelectDropDown(other, _ => { }, options);
                }
                gui.BuildText(".");
            }
        }

        using (gui.EnterRow(allocator: RectAllocator.RightRow)) {
            if (gui.BuildButton("Done")) {
                Close();
            }

            if (gui.BuildButton("Decompose", active: !decomposed)) {
                Decompose();
            }

            if (gui.BuildButton("Export to blueprint", SchemeColor.Grey)) {
                gui.ShowDropDown(ExportBlueprintDropdown);
            }
        }
    }

    private List<(IObjectWithQuality<T>, int)> ExportGoods<T>() where T : Goods {
        List<(IObjectWithQuality<T>, int)> items = [];
        foreach ((IObjectWithQuality<FactorioObject> element, float amount) in list.data) {
            int rounded = MathUtils.Round(amount);
            if (rounded == 0) {
                continue;
            }

            if (element is IObjectWithQuality<T> g) {
                items.Add((g, rounded));
            }
            else if (element is IObjectWithQuality<Entity> e && e.target.itemsToPlace.Length > 0) {
                items.Add((((T)(object)e.target.itemsToPlace[0]).With(e.quality), rounded));
            }
        }

        return items;
    }

    private void ExportBlueprintDropdown(ImGui gui) {
        gui.BuildText("Blueprint string will be copied to clipboard", TextBlockDisplayStyle.WrappedText);
        if (Database.objectsByTypeName.TryGetValue("Entity.constant-combinator", out var combinator)
            && gui.BuildFactorioObjectButtonWithText(combinator) == Click.Left && gui.CloseDropdown()) {

            _ = BlueprintUtilities.ExportConstantCombinators("Shopping list", ExportGoods<Goods>());
        }

        foreach (var container in Database.allContainers) {
            if (container.logisticMode == "requester" && gui.BuildFactorioObjectButtonWithText(container) == Click.Left && gui.CloseDropdown()) {
                _ = BlueprintUtilities.ExportRequesterChests("Shopping list", ExportGoods<Item>(), container);
            }
        }
    }

    private static Recipe? FindSingleProduction(Recipe[] production) {
        Recipe? current = null;
        foreach (Recipe recipe in production) {
            if (recipe.IsAccessible()) {
                if (current != null) {
                    return null;
                }

                current = recipe;
            }
        }

        return current;
    }

    private void Decompose() {
        decomposed = true;
        Queue<IObjectWithQuality<FactorioObject>> decompositionQueue = [];
        Dictionary<IObjectWithQuality<FactorioObject>, float> decomposeResult = [];

        void addDecomposition(FactorioObject obj, Quality quality, float amount) {
            IObjectWithQuality<FactorioObject> key = obj.With(quality);
            if (!decomposeResult.TryGetValue(key, out float prev)) {
                decompositionQueue.Enqueue(key);
            }

            decomposeResult[key] = prev + amount;
        }
        foreach (((FactorioObject obj, Quality quality), float count) in list.data) {
            addDecomposition(obj, quality, count);
        }

        int steps = 0;
        while (decompositionQueue.Count > 0) {
            var elem = decompositionQueue.Dequeue();
            float amount = decomposeResult[elem];
            if (elem.target is Entity e && e.itemsToPlace.Length == 1) {
                addDecomposition(e.itemsToPlace[0], elem.quality, amount);
            }
            else if (elem.target is Recipe rec) {
                if (rec.HasIngredientVariants()) {
                    continue;
                }

                foreach (var ingredient in rec.ingredients) {
                    if (ingredient.goods is Fluid) {
                        addDecomposition(ingredient.goods, Quality.Normal, ingredient.amount * amount);
                    }
                    else {
                        addDecomposition(ingredient.goods, elem.quality, ingredient.amount * amount);
                    }
                }
            }
            else if (elem.target is Goods g && (g.usages.Length <= 5 || (g is Item item && (item.factorioType != "item" || item.placeResult != null)))
                && (rec = FindSingleProduction(g.production)!) != null) {

                addDecomposition(g.production[0], elem.quality, amount / rec.GetProductionPerRecipe(g));
            }
            else {
                continue;
            }

            _ = decomposeResult.Remove(elem);
            if (steps++ > 1000) {
                break;
            }
        }

        list.data = [.. decomposeResult.Select(x => (x.Key, x.Value)).OrderByDescending(x => x.Value)];
    }
}
