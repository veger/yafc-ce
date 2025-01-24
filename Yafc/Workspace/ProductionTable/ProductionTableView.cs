using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SDL2;
using Yafc.Blueprints;
using Yafc.Model;
using Yafc.Parser;
using Yafc.UI;

namespace Yafc;

public class ProductionTableView : ProjectPageView<ProductionTable> {
    private readonly FlatHierarchy<RecipeRow, ProductionTable> flatHierarchyBuilder;

    public ProductionTableView() {
        DataGrid<RecipeRow> grid = new DataGrid<RecipeRow>(new RecipePadColumn(this), new RecipeColumn(this), new EntityColumn(this),
            new IngredientsColumn(this), new ProductsColumn(this), new ModulesColumn(this));

        flatHierarchyBuilder = new FlatHierarchy<RecipeRow, ProductionTable>(grid, BuildSummary,
            "This is a nested group. You can drag&drop recipes here. Nested groups can have their own linked materials.");
    }

    /// <param name="widthStorage">If not <see langword="null"/>, names an instance property in <see cref="Preferences"/> that will be used to store the width of this column.
    /// If the current value of the property is out of range, the initial width will be <paramref name="initialWidth"/>.</param>
    private abstract class ProductionTableDataColumn(ProductionTableView view, string header, float initialWidth, float minWidth = 0, float maxWidth = 0, bool hasMenu = true, string? widthStorage = null)
        : TextDataColumn<RecipeRow>(header, initialWidth, minWidth, maxWidth, hasMenu, widthStorage) {

        protected readonly ProductionTableView view = view;
    }

    private class RecipePadColumn(ProductionTableView view) : ProductionTableDataColumn(view, "", 3f, hasMenu: false) {
        public override void BuildElement(ImGui gui, RecipeRow row) {
            gui.allocator = RectAllocator.Center;
            gui.spacing = 0f;

            if (row.subgroup != null) {
                if (gui.BuildButton(row.subgroup.expanded ? Icon.ChevronDown : Icon.ChevronRight)) {
                    if (InputSystem.Instance.control) {
                        toggleAll(!row.subgroup.expanded, view.model);
                    }
                    else {
                        row.subgroup.RecordChange().expanded = !row.subgroup.expanded;
                    }

                    view.flatHierarchyBuilder.SetData(view.model);
                }
            }

            if (row.warningFlags != 0) {
                bool isError = row.warningFlags >= WarningFlags.EntityNotSpecified;
                bool hover;

                if (isError) {
                    hover = gui.BuildRedButton(Icon.Error, invertedColors: true) == ButtonEvent.MouseOver;
                }
                else {
                    using (gui.EnterGroup(ImGuiUtils.DefaultIconPadding)) {
                        gui.BuildIcon(Icon.Help);
                    }

                    hover = gui.BuildButton(gui.lastRect, SchemeColor.None, SchemeColor.Grey) == ButtonEvent.MouseOver;
                }

                if (hover) {
                    gui.ShowTooltip(g => {
                        if (isError) {
                            g.boxColor = SchemeColor.Error;
                            g.textColor = SchemeColor.ErrorText;
                        }
                        foreach (var (flag, text) in WarningsMeaning) {
                            if ((row.warningFlags & flag) != 0) {
                                g.BuildText(text, TextBlockDisplayStyle.WrappedText);
                            }
                        }
                    });
                }
            }
            else {
                if (row.tag != 0) {
                    BuildRowMarker(gui, row);
                }
            }

            static void toggleAll(bool state, ProductionTable table) {
                foreach (var subgroup in table.recipes.Select(r => r.subgroup).WhereNotNull()) {
                    subgroup.RecordChange().expanded = state;
                    toggleAll(state, subgroup);
                }
            }
        }

        private static void BuildRowMarker(ImGui gui, RecipeRow row) {
            int markerId = row.tag;

            if (markerId < 0 || markerId >= tagIcons.Length) {
                markerId = 0;
            }

            var (icon, color) = tagIcons[markerId];
            gui.BuildIcon(icon, color: color);

            if (gui.BuildButton(gui.lastRect, SchemeColor.None, SchemeColor.BackgroundAlt)) {
                gui.ShowDropDown(imGui => DrawRecipeTagSelect(imGui, row));
            }
        }
    }

    private class RecipeColumn(ProductionTableView view) : ProductionTableDataColumn(view, "Recipe", 13f, 13f, 30f, widthStorage: nameof(Preferences.recipeColumnWidth)) {
        public override void BuildElement(ImGui gui, RecipeRow recipe) {
            gui.spacing = 0.5f;
            switch (gui.BuildFactorioObjectButton(recipe.recipe, ButtonDisplayStyle.ProductionTableUnscaled)) {
                case Click.Left:
                    gui.ShowDropDown(delegate (ImGui imgui) {
                        DrawRecipeTagSelect(imgui, recipe);

                        if (recipe.subgroup == null && imgui.BuildButton("Create nested table") && imgui.CloseDropdown()) {
                            recipe.RecordUndo().subgroup = new ProductionTable(recipe);
                        }

                        if (recipe.subgroup != null && imgui.BuildButton("Add nested desired product") && imgui.CloseDropdown()) {
                            AddDesiredProductAtLevel(recipe.subgroup);
                        }

                        if (recipe.subgroup != null) {
                            BuildRecipeButton(imgui, recipe.subgroup);
                        }

                        if (recipe.subgroup != null && imgui.BuildButton("Unpack nested table").WithTooltip(imgui, recipe.subgroup.expanded ? "Shortcut: right-click" : "Shortcut: Expand, then right-click") && imgui.CloseDropdown()) {
                            unpackNestedTable();
                        }

                        if (recipe.subgroup != null && imgui.BuildButton("ShoppingList") && imgui.CloseDropdown()) {
                            view.BuildShoppingList(recipe);
                        }

                        if (imgui.BuildCheckBox("Show total Input/Output", recipe.showTotalIO, out bool newShowTotalIO)) {
                            recipe.RecordUndo().showTotalIO = newShowTotalIO;
                        }

                        if (imgui.BuildCheckBox("Enabled", recipe.enabled, out bool newEnabled)) {
                            recipe.RecordUndo().enabled = newEnabled;
                        }

                        BuildFavorites(imgui, recipe.recipe, "Add recipe to favorites");

                        if (recipe.subgroup != null && imgui.BuildRedButton("Delete nested table").WithTooltip(imgui, recipe.subgroup.expanded ? "Shortcut: Collapse, then right-click" : "Shortcut: right-click") && imgui.CloseDropdown()) {
                            _ = recipe.owner.RecordUndo().recipes.Remove(recipe);
                        }

                        if (recipe.subgroup == null && imgui.BuildRedButton("Delete recipe").WithTooltip(imgui, "Shortcut: right-click") && imgui.CloseDropdown()) {
                            _ = recipe.owner.RecordUndo().recipes.Remove(recipe);
                        }
                    });
                    break;
                case Click.Right when recipe.subgroup?.expanded ?? false: // With expanded subgroup
                    unpackNestedTable();
                    break;
                case Click.Right: // With collapsed or no subgroup
                    _ = recipe.owner.RecordUndo().recipes.Remove(recipe);
                    break;
            }

            if (!recipe.enabled) {
                gui.textColor = SchemeColor.BackgroundTextFaint;
            }
            else if (view.flatHierarchyBuilder.nextRowIsHighlighted) {
                gui.textColor = view.flatHierarchyBuilder.nextRowTextColor;
            }
            else {
                gui.textColor = recipe.hierarchyEnabled ? SchemeColor.BackgroundText : SchemeColor.BackgroundTextFaint;
            }

            gui.BuildText(recipe.recipe.locName, TextBlockDisplayStyle.WrappedText);

            void unpackNestedTable() {
                var evacuate = recipe.subgroup.recipes;
                _ = recipe.subgroup.RecordUndo();
                recipe.RecordUndo().subgroup = null;
                int index = recipe.owner.recipes.IndexOf(recipe);

                foreach (var evacRecipe in evacuate) {
                    evacRecipe.SetOwner(recipe.owner);
                }

                recipe.owner.RecordUndo().recipes.InsertRange(index + 1, evacuate);
            }
        }

        private static void RemoveZeroRecipes(ProductionTable productionTable) {
            _ = productionTable.RecordUndo().recipes.RemoveAll(x => x.subgroup == null && x.recipesPerSecond == 0);

            foreach (var recipe in productionTable.recipes) {
                if (recipe.subgroup != null) {
                    RemoveZeroRecipes(recipe.subgroup);
                }
            }
        }

        public override void BuildMenu(ImGui gui) {
            BuildRecipeButton(gui, view.model);

            gui.BuildText("Export inputs and outputs to blueprint with constant combinators:", TextBlockDisplayStyle.WrappedText);
            using (gui.EnterRow()) {
                gui.BuildText("Amount per:");

                if (gui.BuildLink("second") && gui.CloseDropdown()) {
                    ExportIo(1f);
                }

                if (gui.BuildLink("minute") && gui.CloseDropdown()) {
                    ExportIo(60f);
                }

                if (gui.BuildLink("hour") && gui.CloseDropdown()) {
                    ExportIo(3600f);
                }
            }

            if (gui.BuildButton("Remove all zero-building recipes") && gui.CloseDropdown()) {
                RemoveZeroRecipes(view.model);
            }

            if (gui.BuildRedButton("Clear recipes") && gui.CloseDropdown()) {
                view.model.RecordUndo().recipes.Clear();
            }

            if (InputSystem.Instance.control && gui.BuildButton("Add ALL recipes") && gui.CloseDropdown()) {
                foreach (var recipe in Database.recipes.all) {
                    if (!recipe.IsAccessible()) {
                        continue;
                    }

                    foreach (var ingredient in recipe.ingredients) {
                        if (ingredient.goods.production.Length == 0) {
                            // 'goto' is a readable way to break out of a nested loop.
                            // See https://stackoverflow.com/questions/324831/breaking-out-of-a-nested-loop
                            goto goodsHaveNoProduction;
                        }
                    }
                    foreach (var product in recipe.products) {
                        view.CreateLink(view.model, product.goods);
                    }

                    view.model.AddRecipe(recipe, DefaultVariantOrdering);
goodsHaveNoProduction:;
                }
            }
        }

        /// <summary>
        /// Build the "Add raw recipe" button and handle its clicks.
        /// </summary>
        /// <param name="table">The table that will receive the new recipes or technologies, if any are selected</param>
        private static void BuildRecipeButton(ImGui gui, ProductionTable table) {
            if (gui.BuildButton("Add raw recipe").WithTooltip(gui, "Ctrl-click to add a technology instead") && gui.CloseDropdown()) {
                if (InputSystem.Instance.control) {
                    SelectMultiObjectPanel.Select(Database.technologies.all, "Select technology",
                        r => table.AddRecipe(r, DefaultVariantOrdering), checkMark: r => table.recipes.Any(rr => rr.recipe == r));
                }
                else {
                    SelectMultiObjectPanel.Select(Database.recipes.explorable, "Select raw recipe",
                        r => table.AddRecipe(r, DefaultVariantOrdering), checkMark: r => table.recipes.Any(rr => rr.recipe == r));
                }
            }
        }

        private void ExportIo(float multiplier) {
            List<(Goods, int)> goods = [];
            foreach (var link in view.model.links) {
                int rounded = MathUtils.Round(link.amount * multiplier);

                if (rounded == 0) {
                    continue;
                }

                goods.Add((link.goods, rounded));
            }

            foreach (var flow in view.model.flow) {
                int rounded = MathUtils.Round(flow.amount * multiplier);

                if (rounded == 0) {
                    continue;
                }

                goods.Add((flow.goods, rounded));
            }

            _ = BlueprintUtilities.ExportConstantCombinators(view.projectPage!.name, goods); // null-forgiving: An active view always has an active page.
        }
    }

    private class EntityColumn(ProductionTableView view) : ProductionTableDataColumn(view, "Entity", 8f) {
        public override void BuildElement(ImGui gui, RecipeRow recipe) {
            if (recipe.isOverviewMode) {
                return;
            }

            Click click;
            using (var group = gui.EnterGroup(default, RectAllocator.Stretch, spacing: 0f)) {
                group.SetWidth(3f);
                if (recipe is { fixedBuildings: > 0, fixedFuel: false, fixedIngredient: null, fixedProduct: null, hierarchyEnabled: true }) {
                    DisplayAmount amount = recipe.fixedBuildings;
                    GoodsWithAmountEvent evt = gui.BuildFactorioObjectWithEditableAmount(recipe.entity, amount, ButtonDisplayStyle.ProductionTableUnscaled,
                        setKeyboardFocus: recipe.ShouldFocusFixedCountThisTime());

                    if (evt == GoodsWithAmountEvent.TextEditing && amount.Value >= 0) {
                        recipe.RecordUndo().fixedBuildings = amount.Value;
                    }

                    click = (Click)evt;
                }
                else {
                    click = gui.BuildFactorioObjectWithAmount(recipe.entity, recipe.buildingCount, ButtonDisplayStyle.ProductionTableUnscaled);
                }

                if (recipe.builtBuildings != null) {
                    DisplayAmount amount = recipe.builtBuildings.Value;

                    if (gui.BuildFloatInput(amount, TextBoxDisplayStyle.FactorioObjectInput with { ColorGroup = SchemeColorGroup.Grey }, recipe.ShouldFocusBuiltCountThisTime())
                        && amount.Value >= 0) {

                        recipe.RecordUndo().builtBuildings = (int)amount.Value;
                    }
                }
            }

            if (recipe.recipe.crafters.Length == 0) {
                // ignore all clicks
            }
            else if (click == Click.Left) {
                ShowEntityDropdown(gui, recipe);
            }
            else if (click == Click.Right) {
                // null-forgiving: We know recipe.recipe.crafters is not empty, so AutoSelect can't return null.
                EntityCrafter favoriteCrafter = recipe.recipe.crafters.AutoSelect(DataUtils.FavoriteCrafter)!;

                if (favoriteCrafter != null && recipe.entity?.target != favoriteCrafter) {
                    _ = recipe.RecordUndo();
                    recipe.entity = new(favoriteCrafter, recipe.entity?.quality ?? Quality.MaxAccessible);

                    if (!recipe.entity.target.energy.fuels.Contains(recipe.fuel)) {
                        recipe.fuel = recipe.entity.target.energy.fuels.AutoSelect(DataUtils.FavoriteFuel);
                    }
                }
                else if (recipe.fixedBuildings > 0) {
                    recipe.RecordUndo().fixedBuildings = 0;
                    // Clear the keyboard focus: If we hide and then recreate the edit box without removing the focus, the UI system will restore the old value.
                    // (If the focus was on a text box we aren't hiding, other code also removes the focus.)
                    // To observe (prior to this fix), add a fixed or built count with a non-default value, clear it with right-click, and then click the "Set ... building count" button again.
                    // The old behavior is that the non-default value is restored.
                    InputSystem.Instance.currentKeyboardFocus?.FocusChanged(false);
                }
                else if (recipe.builtBuildings != null) {
                    recipe.RecordUndo().builtBuildings = null;
                    // Clear the keyboard focus: as above
                    InputSystem.Instance.currentKeyboardFocus?.FocusChanged(false);
                }
            }

            gui.AllocateSpacing(0.5f);
            if (recipe.fuel != Database.voidEnergy || recipe.entity == null || recipe.entity.target.energy.type != EntityEnergyType.Void) {
                var (fuel, fuelAmount, fuelLink, _) = recipe.FuelInformation;
                view.BuildGoodsIcon(gui, fuel, fuelLink, fuelAmount, ProductDropdownType.Fuel, recipe, recipe.linkRoot, HintLocations.OnProducingRecipes);
            }
            else {
                if (recipe.recipe == Database.electricityGeneration && recipe.entity.target.factorioType is "solar-panel" or "lightning-attractor") {
                    BuildAccumulatorView(gui, recipe);
                }
            }
        }

        private static void BuildAccumulatorView(ImGui gui, RecipeRow recipe) {
            var accumulator = recipe.GetVariant(Database.allAccumulators);
            Quality accumulatorQuality = recipe.GetVariant(Database.qualities.all.OrderBy(q => q.level).ToArray());
            float requiredAccumulators = 0;
            if (recipe.entity?.target.factorioType == "solar-panel") {
                float requiredMj = recipe.entity?.GetCraftingSpeed() * recipe.buildingCount * (70 / 0.7f) ?? 0; // 70 seconds of charge time to last through the night
                requiredAccumulators = requiredMj / accumulator.AccumulatorCapacity(accumulatorQuality);
            }
            else if (recipe.entity.Is(out IObjectWithQuality<EntityAttractor>? attractor)) {
                // Model the storm as rising from 0% to 100% over 30 seconds, staying at 100% for 24 seconds, and decaying over 30 seconds.
                // I adjusted these until the right answers came out of my Excel model.
                // TODO(multi-planet): Adjust these numbers based on day length.
                const int stormRiseTicks = 30 * 60, stormPlateauTicks = 24 * 60, stormFallTicks = 30 * 60;
                const int stormTotalTicks = stormRiseTicks + stormPlateauTicks + stormFallTicks;

                // Don't try to model the storm with less than 1 attractor (6 lightning strikes for a normal rod)
                float stormMjPerTick = attractor.StormPotentialPerTick() * (recipe.buildingCount < 1 ? 1 : recipe.buildingCount);
                // TODO(multi-planet): Use the appropriate LightningPrototype::energy instead of hardcoding the 1000 of Fulgoran lightning.
                // Tick numbers will be wrong if the first and last strike don't happen in the rise and fall periods. This is okay because
                // a single normal rod has the first strike at 23 seconds, and the _second_ at 32.
                float totalStormEnergy = stormMjPerTick * 3 * 60 * 60 /*TODO(multi-planet): ticks per day*/ * 0.3f;
                float lostStormEnergy = totalStormEnergy % 1000;
                float firstStrikeTick = (MathF.Sqrt(1 + 8 * 1000 * stormRiseTicks / stormMjPerTick) + 1) / 2;
                float lastStrikeTick = stormTotalTicks - (MathF.Sqrt(1 + 8 * lostStormEnergy * stormFallTicks / stormMjPerTick) + 1) / 2;
                int strikeCount = (int)(totalStormEnergy / 1000);

                float requiredPower = attractor.GetCraftingSpeed() * recipe.buildingCount;

                // Two different conditions need to be tested here. The first test is for capacity when discharging: the accumulators must have
                // a capacity of requiredPower * timeBetween(lastStrikeDischarged, firstStrike + 1 day)
                // As simplifying assumptions for this calculation, (1) the accumulators are fully charged when the last strike hits, and
                // (2) the attractor's internal buffer is empty when the last strike hits.
                // If incorrect, these cause errors in opposite directions.
                float lastStrikeDrainedTick = lastStrikeTick + 1000 * attractor.GetAttractorEfficiency() / (requiredPower + attractor.target.drain) * 60;
                float requiredTicks = 3 * 60 * 60 /*TODO(multi-planet): ticks per day*/ - lastStrikeDrainedTick + firstStrikeTick;
                float requiredMj = requiredPower * requiredTicks / 60;

                // The second test is for capacity when charging: The accumulators must draw at least requiredMj out of the attractors.
                // Solve: chargeTimePerStrike = 1000MJ * effectiveness / (150MW + chargePower + requiredPower)
                // And: chargePower * chargeTimePerStrike * #strikes - requiredPower * nonStrikeStormTime = requiredMj
                // Not fun (see Fulgora lightning model.md), but the result is:
                float stormLengthSeconds = (lastStrikeDrainedTick - firstStrikeTick) / 60;
                float stormEnergy = 1000 * attractor.GetAttractorEfficiency() * strikeCount;
                float numerator = requiredMj * attractor.target.drain + requiredPower * (requiredMj + stormLengthSeconds * (attractor.target.drain + requiredPower) - stormEnergy);
                float denominator = stormEnergy - requiredPower * stormLengthSeconds - requiredMj;
                float requiredChargeMw = numerator / denominator;

                requiredAccumulators = Math.Max(requiredMj / accumulator.AccumulatorCapacity(accumulatorQuality),
                    requiredChargeMw / accumulator.Power(accumulatorQuality));
            }

            ObjectWithQuality<Entity> accumulatorWithQuality = new(accumulator, accumulatorQuality);
            if (gui.BuildFactorioObjectWithAmount(accumulatorWithQuality, requiredAccumulators, ButtonDisplayStyle.ProductionTableUnscaled) == Click.Left) {
                ShowAccumulatorDropdown(gui, recipe, accumulator, accumulatorQuality);
            }
        }

        private static void ShowAccumulatorDropdown(ImGui gui, RecipeRow recipe, Entity currentAccumulator, Quality accumulatorQuality)
            => gui.BuildObjectQualitySelectDropDown(Database.allAccumulators,
                newAccumulator => recipe.RecordUndo().ChangeVariant(currentAccumulator, newAccumulator.target),
                new("Select accumulator", ExtraText: x => DataUtils.FormatAmount(x.AccumulatorCapacity(accumulatorQuality), UnitOfMeasure.Megajoule)),
                accumulatorQuality,
                newQuality => recipe.RecordUndo().ChangeVariant(accumulatorQuality, newQuality));

        private static void ShowEntityDropdown(ImGui gui, RecipeRow recipe) {
            Quality quality = recipe.entity?.quality ?? Quality.Normal;
            gui.ShowDropDown(gui => {
                EntityCrafter? favoriteCrafter = recipe.recipe.crafters.AutoSelect(DataUtils.FavoriteCrafter);
                if (favoriteCrafter == recipe.entity?.target) { favoriteCrafter = null; }
                bool willResetFixed = favoriteCrafter == null, willResetBuilt = willResetFixed && recipe.fixedBuildings == 0;

                if (recipe.entity == null) {
                    _ = gui.BuildQualityList(quality, out quality);
                }
                else if (gui.BuildQualityList(recipe.entity.quality, out quality) && gui.CloseDropdown()) {
                    if (quality == recipe.entity.quality) {
                        return;
                    }
                    recipe.RecordUndo().entity = recipe.entity.With(quality);
                }

                gui.BuildInlineObjectListAndButton(recipe.recipe.crafters, sel => {
                    if (recipe.entity?.target == sel) {
                        return;
                    }

                    _ = recipe.RecordUndo();
                    recipe.entity = new(sel, quality);
                    if (!sel.energy.fuels.Contains(recipe.fuel)) {
                        recipe.fuel = recipe.entity.target.energy.fuels.AutoSelect(DataUtils.FavoriteFuel);
                    }
                }, new("Select crafting entity", DataUtils.FavoriteCrafter, ExtraText: x => DataUtils.FormatAmount(x.CraftingSpeed(quality), UnitOfMeasure.Percent)));

                gui.AllocateSpacing(0.5f);

                if (recipe.fixedBuildings > 0f && (recipe.fixedFuel || recipe.fixedIngredient != null || recipe.fixedProduct != null || !recipe.hierarchyEnabled)) {
                    ButtonEvent evt = gui.BuildButton("Clear fixed recipe multiplier");
                    if (willResetFixed) {
                        _ = evt.WithTooltip(gui, "Shortcut: right-click");
                    }
                    if (evt && gui.CloseDropdown()) {
                        recipe.RecordUndo().fixedBuildings = 0f;
                    }
                }

                if (recipe.hierarchyEnabled) {
                    string fixedBuildingsTip = "Tell YAFC how many buildings it must use when solving this page.\n" +
                        "Use this to ask questions like 'What does it take to handle the output of ten miners?'";

                    using (gui.EnterRowWithHelpIcon(fixedBuildingsTip)) {
                        gui.allocator = RectAllocator.RemainingRow;
                        if (recipe.fixedBuildings > 0f && !recipe.fixedFuel && recipe.fixedIngredient == null && recipe.fixedProduct == null) {
                            ButtonEvent evt = gui.BuildButton("Clear fixed building count");

                            if (willResetFixed) {
                                _ = evt.WithTooltip(gui, "Shortcut: right-click");
                            }

                            if (evt && gui.CloseDropdown()) {
                                recipe.RecordUndo().fixedBuildings = 0f;
                            }
                        }
                        else if (gui.BuildButton("Set fixed building count") && gui.CloseDropdown()) {
                            recipe.RecordUndo().fixedBuildings = recipe.buildingCount <= 0f ? 1f : recipe.buildingCount;
                            recipe.fixedFuel = false;
                            recipe.fixedIngredient = null;
                            recipe.fixedProduct = null;
                            recipe.FocusFixedCountOnNextDraw();
                        }
                    }
                }

                using (gui.EnterRowWithHelpIcon("Tell YAFC how many of these buildings you have in your factory.\nYAFC will warn you if you need to build more buildings.")) {
                    gui.allocator = RectAllocator.RemainingRow;

                    if (recipe.builtBuildings != null) {
                        ButtonEvent evt = gui.BuildButton("Clear built building count");

                        if (willResetBuilt) {
                            _ = evt.WithTooltip(gui, "Shortcut: right-click");
                        }

                        if (evt && gui.CloseDropdown()) {
                            recipe.RecordUndo().builtBuildings = null;
                        }
                    }
                    else if (gui.BuildButton("Set built building count") && gui.CloseDropdown()) {
                        recipe.RecordUndo().builtBuildings = Math.Max(0, Convert.ToInt32(Math.Ceiling(recipe.buildingCount)));
                        recipe.FocusBuiltCountOnNextDraw();
                    }
                }

                if (recipe.entity != null) {
                    using (gui.EnterRowWithHelpIcon("Generate a blueprint for one of these buildings, with the recipe and internal modules set.")) {
                        gui.allocator = RectAllocator.RemainingRow;

                        if (gui.BuildButton("Create single building blueprint") && gui.CloseDropdown()) {
                            BlueprintEntity entity = new BlueprintEntity { index = 1, name = recipe.entity.target.name };

                            if (recipe.recipe is not Mechanics) {
                                entity.recipe = recipe.recipe.name;
                            }

                            var modules = recipe.usedModules.modules;

                            if (modules != null) {
                                int idx = 0;
                                foreach (var (module, count, beacon) in modules) {
                                    if (!beacon) {
                                        BlueprintItem item = new BlueprintItem { id = { name = module.target.name, quality = module.quality.name } };
                                        item.items.inInventory.AddRange(Enumerable.Range(idx, count).Select(i => new BlueprintInventoryItem { stack = i }));
                                        entity.items.Add(item);
                                        idx += count;
                                    }
                                }
                            }
                            BlueprintString bp = new BlueprintString(recipe.recipe.locName) { blueprint = { entities = { entity } } };
                            _ = SDL.SDL_SetClipboardText(bp.ToBpString());
                        }
                    }

                    if (recipe.recipe.crafters.Length > 1) {
                        BuildFavorites(gui, recipe.entity.target, "Add building to favorites");
                    }
                }
            });
        }

        public override void BuildMenu(ImGui gui) {
            if (gui.BuildButton("Mass set assembler") && gui.CloseDropdown()) {
                SelectSingleObjectPanel.Select(Database.allCrafters, "Set assembler for all recipes", set => {
                    DataUtils.FavoriteCrafter.AddToFavorite(set, 10);

                    foreach (var recipe in view.GetRecipesRecursive()) {
                        if (recipe.recipe.crafters.Contains(set)) {
                            _ = recipe.RecordUndo();
                            recipe.entity = new(set, recipe.entity?.quality ?? Quality.Normal);

                            if (!set.energy.fuels.Contains(recipe.fuel)) {
                                recipe.fuel = recipe.entity.target.energy.fuels.AutoSelect(DataUtils.FavoriteFuel);
                            }
                        }
                    }
                }, DataUtils.FavoriteCrafter);
            }

            if (gui.BuildQualityList(null, out Quality? quality, "Mass set quality") && gui.CloseDropdown()) {
                foreach (RecipeRow recipe in view.GetRecipesRecursive()) {
                    recipe.RecordUndo().entity = recipe.entity?.With(quality);
                }
            }

            if (gui.BuildButton("Mass set fuel") && gui.CloseDropdown()) {
                SelectSingleObjectPanel.Select(Database.goods.all.Where(x => x.fuelValue > 0), "Set fuel for all recipes", set => {
                    DataUtils.FavoriteFuel.AddToFavorite(set, 10);

                    foreach (var recipe in view.GetRecipesRecursive()) {
                        if (recipe.entity != null && recipe.entity.target.energy.fuels.Contains(set)) {
                            recipe.RecordUndo().fuel = set;
                        }
                    }
                }, DataUtils.FavoriteFuel);
            }

            if (gui.BuildButton("Shopping list") && gui.CloseDropdown()) {
                view.BuildShoppingList(null);
            }
        }
    }

    private class IngredientsColumn(ProductionTableView view) : ProductionTableDataColumn(view, "Ingredients", 32f, 16f, 100f, hasMenu: false, nameof(Preferences.ingredientsColumWidth)) {
        public override void BuildElement(ImGui gui, RecipeRow recipe) {
            var grid = gui.EnterInlineGrid(3f, 1f);

            if (recipe.isOverviewMode) {
                view.BuildTableIngredients(gui, recipe.subgroup, recipe.owner, ref grid);
            }
            else {
                foreach (var (goods, amount, link, variants) in recipe.Ingredients) {
                    grid.Next();
                    view.BuildGoodsIcon(gui, goods, link, amount, ProductDropdownType.Ingredient, recipe, recipe.linkRoot, HintLocations.OnProducingRecipes, variants);
                }
                if (recipe.fixedIngredient == Database.itemInput || recipe.showTotalIO) {
                    grid.Next();
                    view.BuildGoodsIcon(gui, recipe.hierarchyEnabled ? Database.itemInput : null, null, recipe.Ingredients.Where(i => i.Goods is Item).Sum(i => i.Amount),
                        ProductDropdownType.Ingredient, recipe, recipe.linkRoot, HintLocations.None);
                }
            }
            grid.Dispose();
        }
    }

    private class ProductsColumn(ProductionTableView view) : ProductionTableDataColumn(view, "Products", 12f, 10f, 70f, hasMenu: false, nameof(Preferences.productsColumWidth)) {
        public override void BuildElement(ImGui gui, RecipeRow recipe) {
            var grid = gui.EnterInlineGrid(3f, 1f);
            if (recipe.isOverviewMode) {
                view.BuildTableProducts(gui, recipe.subgroup, recipe.owner, ref grid, false);
            }
            else {
                foreach (var (goods, amount, link, percentSpoiled) in recipe.Products) {
                    grid.Next();
                    if (recipe.recipe is Recipe { preserveProducts: true }) {
                        view.BuildGoodsIcon(gui, goods, link, amount, ProductDropdownType.Product, recipe, recipe.linkRoot, new() {
                            HintLocations = HintLocations.OnConsumingRecipes,
                            ExtraSpoilInformation = gui => gui.BuildText("This recipe output does not start spoiling until removed from the machine.", TextBlockDisplayStyle.WrappedText)
                        });
                    }
                    else if (percentSpoiled == null) {
                        view.BuildGoodsIcon(gui, goods, link, amount, ProductDropdownType.Product, recipe, recipe.linkRoot, HintLocations.OnConsumingRecipes);
                    }
                    else if (percentSpoiled == 0) {
                        view.BuildGoodsIcon(gui, goods, link, amount, ProductDropdownType.Product, recipe, recipe.linkRoot,
                            new() { HintLocations = HintLocations.OnConsumingRecipes, ExtraSpoilInformation = gui => gui.BuildText("This recipe output is always fresh.") });
                    }
                    else {
                        view.BuildGoodsIcon(gui, goods, link, amount, ProductDropdownType.Product, recipe, recipe.linkRoot, new() {
                            HintLocations = HintLocations.OnConsumingRecipes,
                            ExtraSpoilInformation = gui => gui.BuildText($"This recipe output is {DataUtils.FormatAmount(percentSpoiled.Value, UnitOfMeasure.Percent)} spoiled.")
                        });
                    }
                }
                if (recipe.fixedProduct == Database.itemOutput || recipe.showTotalIO) {
                    grid.Next();
                    view.BuildGoodsIcon(gui, recipe.hierarchyEnabled ? Database.itemOutput : null, null, recipe.Products.Where(i => i.Goods is Item).Sum(i => i.Amount),
                        ProductDropdownType.Product, recipe, recipe.linkRoot, HintLocations.None);
                }
            }
            grid.Dispose();
        }
    }

    private class ModulesColumn : ProductionTableDataColumn {
        private readonly VirtualScrollList<ProjectModuleTemplate> moduleTemplateList;
        private RecipeRow editingRecipeModules = null!; // null-forgiving: This is set as soon as we open a module dropdown.

        public ModulesColumn(ProductionTableView view) : base(view, "Modules", 10f, 7f, 16f, widthStorage: nameof(Preferences.modulesColumnWidth))
            => moduleTemplateList = new VirtualScrollList<ProjectModuleTemplate>(15f, new Vector2(20f, 2.5f), ModuleTemplateDrawer, collapsible: true);

        private void ModuleTemplateDrawer(ImGui gui, ProjectModuleTemplate element, int index) {
            var evt = gui.BuildContextMenuButton(element.name, icon: element.icon?.icon ?? default, disabled: !element.template.IsCompatibleWith(editingRecipeModules));

            if (evt == ButtonEvent.Click && gui.CloseDropdown()) {
                var copied = JsonUtils.Copy(element.template, editingRecipeModules, null);
                editingRecipeModules.RecordUndo().modules = copied;
                view.Rebuild();
            }
            else if (evt == ButtonEvent.MouseOver) {
                ShowModuleTemplateTooltip(gui, element.template);
            }
        }

        public override void BuildElement(ImGui gui, RecipeRow recipe) {
            if (recipe.isOverviewMode) {
                return;
            }

            if (recipe.entity == null || recipe.entity.target.allowedEffects == AllowedEffects.None || recipe.entity.target.allowedModuleCategories is []) {
                return;
            }

            using var grid = gui.EnterInlineGrid(3f);
            if (recipe.usedModules.modules == null || recipe.usedModules.modules.Length == 0) {
                drawItem(gui, null, 0);
            }
            else {
                bool wasBeacon = false;

                foreach (var (module, count, beacon) in recipe.usedModules.modules) {
                    if (beacon && !wasBeacon) {
                        wasBeacon = true;

                        if (recipe.usedModules.beacon != null) {
                            drawItem(gui, recipe.usedModules.beacon, recipe.usedModules.beaconCount);
                        }
                    }
                    drawItem(gui, module, count);
                }
            }

            void drawItem(ImGui gui, IObjectWithQuality<FactorioObject>? item, int count) {
                grid.Next();
                switch (gui.BuildFactorioObjectWithAmount(item, count, ButtonDisplayStyle.ProductionTableUnscaled)) {
                    case Click.Left:
                        ShowModuleDropDown(gui, recipe);
                        break;
                    case Click.Right when recipe.modules != null:
                        recipe.RecordUndo().RemoveFixedModules();
                        break;
                }
            }
        }

        private void ShowModuleTemplateTooltip(ImGui gui, ModuleTemplate template) => gui.ShowTooltip(imGui => {
            if (!template.IsCompatibleWith(editingRecipeModules)) {
                imGui.BuildText("This module template seems incompatible with the recipe or the building", TextBlockDisplayStyle.WrappedText);
            }

            using var grid = imGui.EnterInlineGrid(3f, 1f);
            foreach (var module in template.list) {
                grid.Next();
                _ = imGui.BuildFactorioObjectWithAmount(module.module, module.fixedCount, ButtonDisplayStyle.ProductionTableUnscaled);
            }

            if (template.beacon != null) {
                grid.Next();
                _ = imGui.BuildFactorioObjectWithAmount(template.beacon, template.CalcBeaconCount(), ButtonDisplayStyle.ProductionTableUnscaled);
                foreach (var module in template.beaconList) {
                    grid.Next();
                    _ = imGui.BuildFactorioObjectWithAmount(module.module, module.fixedCount, ButtonDisplayStyle.ProductionTableUnscaled);
                }
            }
        });

        private void ShowModuleDropDown(ImGui gui, RecipeRow recipe) {
            Module[] modules = Database.allModules.Where(x => recipe.recipe.CanAcceptModule(x) && (recipe.entity?.target.CanAcceptModule(x.moduleSpecification) ?? false)).ToArray();
            editingRecipeModules = recipe;
            moduleTemplateList.data = [.. Project.current.sharedModuleTemplates
                // null-forgiving: non-nullable collections are happy to report they don't contain null values.
                .Where(x => x.filterEntities.Count == 0 || x.filterEntities.Contains(recipe.entity?.target!))
                .OrderByDescending(x => x.template.IsCompatibleWith(recipe))];

            Quality quality = Quality.Normal;
            gui.ShowDropDown(dropGui => {
                if (recipe.modules != null && dropGui.BuildButton("Use default modules").WithTooltip(dropGui, "Shortcut: right-click") && dropGui.CloseDropdown()) {
                    recipe.RemoveFixedModules();
                }

                if (recipe.entity?.target.moduleSlots > 0) {
                    if (recipe.modules?.list.Count > 0) {
                        quality = recipe.modules.list[0].module.quality;
                        if (dropGui.BuildQualityList(quality, out Quality newQuality) && dropGui.CloseDropdown()) {
                            ModuleTemplateBuilder builder = recipe.modules.GetBuilder();
                            builder.list[0] = builder.list[0] with { module = builder.list[0].module.With(newQuality) };
                            recipe.RecordUndo().modules = builder.Build(recipe);
                        }
                    }
                    else {
                        _ = dropGui.BuildQualityList(quality, out quality);
                    }
                    dropGui.BuildInlineObjectListAndButton(modules, m => recipe.SetFixedModule(new(m, quality)), new("Select fixed module", DataUtils.FavoriteModule));
                }

                if (moduleTemplateList.data.Count > 0) {
                    dropGui.BuildText("Use module template:", Font.subheader);
                    moduleTemplateList.Build(dropGui);
                }
                if (dropGui.BuildButton("Configure module templates") && dropGui.CloseDropdown()) {
                    ModuleTemplateConfiguration.Show();
                }

                if (dropGui.BuildButton("Customize modules") && dropGui.CloseDropdown()) {
                    ModuleCustomizationScreen.Show(recipe);
                }
            });
        }

        public override void BuildMenu(ImGui gui) {
            var model = view.model;

            gui.BuildText("Auto modules", Font.subheader);
            ModuleFillerParametersScreen.BuildSimple(gui, model.modules!); // null-forgiving: owner is a ProjectPage, so modules is not null.
            if (gui.BuildButton("Module settings") && gui.CloseDropdown()) {
                ModuleFillerParametersScreen.Show(model.modules!);
            }
        }
    }

    public static void BuildFavorites(ImGui imgui, FactorioObject? obj, string prompt) {
        if (obj == null) {
            return;
        }

        bool isFavorite = Project.current.preferences.favorites.Contains(obj);
        using (imgui.EnterRow(0.5f, RectAllocator.LeftRow)) {
            imgui.BuildIcon(isFavorite ? Icon.StarFull : Icon.StarEmpty);
            imgui.RemainingRow().BuildText(isFavorite ? "Favorite" : prompt);
        }
        if (imgui.OnClick(imgui.lastRect)) {
            Project.current.preferences.ToggleFavorite(obj);
        }
    }

    public override float CalculateWidth() => flatHierarchyBuilder.width;

    public static void CreateProductionSheet() => ProjectPageSettingsPanel.Show(null, (name, icon) => MainScreen.Instance.AddProjectPage(name, icon, typeof(ProductionTable), true, true));

    private static readonly IComparer<Goods> DefaultVariantOrdering =
        new DataUtils.FactorioObjectComparer<Goods>((x, y) => (y.ApproximateFlow() / MathF.Abs(y.Cost())).CompareTo(x.ApproximateFlow() / MathF.Abs(x.Cost())));

    private enum ProductDropdownType {
        DesiredProduct,
        Fuel,
        Ingredient,
        Product,
        DesiredIngredient,
    }

    private void CreateLink(ProductionTable table, Goods goods) {
        if (table.linkMap.ContainsKey(goods) || !goods.isLinkable) {
            return;
        }

        ProductionLink link = new ProductionLink(table, goods);
        Rebuild();
        table.RecordUndo().links.Add(link);
    }

    private void DestroyLink(ProductionLink link) {
        if (link.owner.links.Contains(link)) {
            _ = link.owner.RecordUndo().links.Remove(link);
            Rebuild();
        }
    }

    private static void CreateNewProductionTable(Goods goods, float amount) {
        var page = MainScreen.Instance.AddProjectPage(goods.locName, goods, typeof(ProductionTable), true, false);
        ProductionTable content = (ProductionTable)page.content;
        ProductionLink link = new ProductionLink(content, goods) { amount = amount > 0 ? amount : 1 };
        content.links.Add(link);
        content.RebuildLinkMap();
    }

    private void OpenProductDropdown(ImGui targetGui, Rect rect, Goods goods, float amount, ProductionLink? link,
        ProductDropdownType type, RecipeRow? recipe, ProductionTable context, Goods[]? variants = null) {

        if (InputSystem.Instance.shift) {
            Project.current.preferences.SetSourceResource(goods, !goods.IsSourceResource());
            targetGui.Rebuild();
            return;
        }

        var comparer = DataUtils.GetRecipeComparerFor(goods);
        HashSet<RecipeOrTechnology> allRecipes = new HashSet<RecipeOrTechnology>(context.recipes.Select(x => x.recipe));

        bool recipeExists(RecipeOrTechnology rec) => allRecipes.Contains(rec);

        Goods? selectedFuel = null;
        Goods? spentFuel = null;

        async void addRecipe(RecipeOrTechnology rec) {
            if (variants == null) {
                CreateLink(context, goods);
            }
            else {
                foreach (var variant in variants) {
                    if (rec.GetProductionPerRecipe(variant) > 0f) {
                        CreateLink(context, variant);

                        if (variant != goods) {
                            // null-forgiving: If variants is not null, neither is recipe: Only the call from BuildGoodsIcon sets variants,
                            // and the only call to BuildGoodsIcon that sets variants also sets recipe.
                            recipe!.RecordUndo().ChangeVariant(goods, variant);
                        }

                        break;
                    }
                }
            }

            if (!allRecipes.Contains(rec) || (await MessageBox.Show("Recipe already exists", $"Add a second copy of {rec.locName}?", "Add a copy", "Cancel")).choice) {
                context.AddRecipe(rec, DefaultVariantOrdering, selectedFuel, spentFuel);
            }
        }

        if (InputSystem.Instance.control) {
            bool isInput = type <= ProductDropdownType.Ingredient;
            var recipeList = isInput ? goods.production : goods.usages;

            if (recipeList.SelectSingle(out _) is Recipe selected) {
                addRecipe(selected);
                return;
            }
        }

        Recipe[] allProduction = variants == null ? goods.production : variants.SelectMany(x => x.production).Distinct().ToArray();

        Recipe[] fuelUseList = [.. goods.fuelFor.OfType<EntityCrafter>()
            .SelectMany(e => e.recipes).OfType<Recipe>()
            .Distinct().OrderBy(e => e, DataUtils.DefaultRecipeOrdering)];

        Recipe[] spentFuelRecipes = [.. goods.miscSources.OfType<Item>()
            .SelectMany(e => e.fuelFor.OfType<EntityCrafter>())
            .SelectMany(e => e.recipes).OfType<Recipe>()
            .Distinct().OrderBy(e => e, DataUtils.DefaultRecipeOrdering)];

        targetGui.ShowDropDown(rect, dropDownContent, new Padding(1f), 25f);

        void dropDownContent(ImGui gui) {
            if (type == ProductDropdownType.Fuel && recipe?.entity != null) {
                EntityEnergy? energy = recipe.entity.target.energy;

                if (energy == null || energy.fuels.Length == 0) {
                    gui.BuildText("This entity has no known fuels");
                }
                else if (energy.fuels.Length > 1 || energy.fuels[0] != recipe.fuel) {
                    Func<Goods, string> fuelDisplayFunc = energy.type == EntityEnergyType.FluidHeat
                         ? g => DataUtils.FormatAmount(g.fluid?.heatValue ?? 0, UnitOfMeasure.Megajoule)
                         : g => DataUtils.FormatAmount(g.fuelValue, UnitOfMeasure.Megajoule);

                    BuildFavorites(gui, recipe.fuel, "Add fuel to favorites");
                    gui.BuildInlineObjectListAndButton(energy.fuels, fuel => recipe.RecordUndo().fuel = fuel,
                        new("Select fuel", DataUtils.FavoriteFuel, ExtraText: fuelDisplayFunc));
                }
            }

            if (variants != null) {
                gui.BuildText("Accepted fluid variants:");
                using (var grid = gui.EnterInlineGrid(3f)) {
                    foreach (var variant in variants) {
                        grid.Next();

                        if (gui.BuildFactorioObjectButton(variant, ButtonDisplayStyle.ProductionTableScaled(variant == goods ? SchemeColor.Primary : SchemeColor.None),
                            tooltipOptions: HintLocations.OnProducingRecipes) == Click.Left && variant != goods) {

                            // null-forgiving: If variants is not null, neither is recipe: Only the call from BuildGoodsIcon sets variants,
                            // and the only call to BuildGoodsIcon that sets variants also sets recipe.
                            recipe!.RecordUndo().ChangeVariant(goods, variant);

                            if (recipe!.fixedIngredient == goods) {
                                recipe.fixedIngredient = variant;
                            }
                            _ = gui.CloseDropdown();
                        }
                    }
                }

                gui.allocator = RectAllocator.Stretch;
            }

            if (link != null) {
                foreach (string warning in link.LinkWarnings) {
                    gui.BuildText(warning, TextBlockDisplayStyle.ErrorText);
                }
            }

            #region Recipe selection
            int numberOfShownRecipes = 0;

            if (goods.name == SpecialNames.ResearchUnit) {
                if (gui.BuildButton("Add technology") && gui.CloseDropdown()) {
                    SelectMultiObjectPanel.Select(Database.technologies.all, "Select technology",
                        r => context.AddRecipe(r, DefaultVariantOrdering), checkMark: r => context.recipes.Any(rr => rr.recipe == r));
                }
            }
            else if (type <= ProductDropdownType.Ingredient && allProduction.Length > 0) {
                gui.BuildInlineObjectListAndButton(allProduction, addRecipe, new("Add production recipe", comparer, 6, true, recipeExists));
                numberOfShownRecipes += allProduction.Length;

                if (link == null) {
                    Rect iconRect = new Rect(gui.lastRect.Right - 2f, gui.lastRect.Top, 2f, 2f);
                    gui.DrawIcon(iconRect.Expand(-0.2f), Icon.OpenNew, gui.textColor);
                    var evt = gui.BuildButton(iconRect, SchemeColor.None, SchemeColor.Grey);

                    if (evt == ButtonEvent.Click && gui.CloseDropdown()) {
                        CreateNewProductionTable(goods, amount);
                    }
                    else if (evt == ButtonEvent.MouseOver) {
                        gui.ShowTooltip(iconRect, "Create new production table for " + goods.locName);
                    }
                }
            }

            if (type <= ProductDropdownType.Ingredient && spentFuelRecipes.Length > 0) {
                gui.BuildInlineObjectListAndButton(
                    spentFuelRecipes,
                    (x) => { spentFuel = goods; addRecipe(x); },
                    new("Produce it as a spent fuel",
                    DataUtils.AlreadySortedRecipe,
                    3,
                    true,
                    recipeExists));
                numberOfShownRecipes += spentFuelRecipes.Length;
            }

            if (type >= ProductDropdownType.Product && goods.usages.Length > 0) {
                gui.BuildInlineObjectListAndButton(
                    goods.usages,
                    addRecipe,
                    new("Add consumption recipe",
                    DataUtils.DefaultRecipeOrdering,
                    6,
                    true,
                    recipeExists));
                numberOfShownRecipes += goods.usages.Length;
            }

            if (type >= ProductDropdownType.Product && fuelUseList.Length > 0) {
                gui.BuildInlineObjectListAndButton(
                    fuelUseList,
                    (x) => { selectedFuel = goods; addRecipe(x); },
                    new("Add fuel usage",
                    DataUtils.AlreadySortedRecipe,
                    6,
                    true,
                    recipeExists));
                numberOfShownRecipes += fuelUseList.Length;
            }

            if (type >= ProductDropdownType.Product && Database.allSciencePacks.Contains(goods)
                && gui.BuildButton("Add consumption technology") && gui.CloseDropdown()) {
                // Select from the technologies that consume this science pack.
                SelectMultiObjectPanel.Select(Database.technologies.all.Where(t => t.ingredients.Select(i => i.goods).Contains(goods)),
                    "Add technology", addRecipe, checkMark: recipeExists);
            }

            if (type >= ProductDropdownType.Product && allProduction.Length > 0) {
                gui.BuildInlineObjectListAndButton(allProduction, addRecipe, new("Add production recipe", comparer, 1, true, recipeExists));
                numberOfShownRecipes += allProduction.Length;
            }

            if (numberOfShownRecipes > 1) {
                gui.BuildText("Hint: ctrl+click to add multiple", TextBlockDisplayStyle.HintText);
            }
            #endregion

            #region Link management
            if (link != null && gui.BuildCheckBox("Allow overproduction", link.algorithm == LinkAlgorithm.AllowOverProduction, out bool newValue)) {
                link.RecordUndo().algorithm = newValue ? LinkAlgorithm.AllowOverProduction : LinkAlgorithm.Match;
            }

            if (link != null && gui.BuildButton("View link summary") && gui.CloseDropdown()) {
                ProductionLinkSummaryScreen.Show(link);
            }

            if (link != null && link.owner == context) {
                if (link.amount != 0) {
                    gui.BuildText(goods.locName + " is a desired product and cannot be unlinked.", TextBlockDisplayStyle.WrappedText);
                }
                else {
                    string goodProdLinkedMessage = goods.locName + " production is currently linked. This means that YAFC will try to match production with consumption.";
                    gui.BuildText(goodProdLinkedMessage, TextBlockDisplayStyle.WrappedText);
                }

                if (type is ProductDropdownType.DesiredIngredient or ProductDropdownType.DesiredProduct) {
                    if (gui.BuildButton("Remove desired product") && gui.CloseDropdown()) {
                        link.RecordUndo().amount = 0;
                    }

                    if (gui.BuildButton("Remove and unlink").WithTooltip(gui, "Shortcut: right-click") && gui.CloseDropdown()) {
                        DestroyLink(link);
                    }
                }
                else if (link.amount == 0 && gui.BuildButton("Unlink").WithTooltip(gui, "Shortcut: right-click") && gui.CloseDropdown()) {
                    DestroyLink(link);
                }
            }
            else if (goods != null) {
                if (link != null) {
                    string goodsNestLinkMessage = goods.locName + " production is currently linked, but the link is outside this nested table. " +
                        "Nested tables can have its own separate set of links";
                    gui.BuildText(goodsNestLinkMessage, TextBlockDisplayStyle.WrappedText);
                }
                else if (goods.isLinkable) {
                    string notLinkedMessage = goods.locName + " production is currently NOT linked. This means that YAFC will make no attempt to match production with consumption.";
                    gui.BuildText(notLinkedMessage, TextBlockDisplayStyle.WrappedText);
                    if (gui.BuildButton("Create link").WithTooltip(gui, "Shortcut: right-click") && gui.CloseDropdown()) {
                        CreateLink(context, goods);
                    }
                }
            }
            #endregion

            #region Fixed production/consumption
            if (goods != null && recipe != null && recipe.hierarchyEnabled) {
                if (recipe.fixedBuildings == 0
                    || (type == ProductDropdownType.Fuel && !recipe.fixedFuel)
                    || (type == ProductDropdownType.Ingredient && recipe.fixedIngredient != goods)
                    || (type == ProductDropdownType.Product && recipe.fixedProduct != goods)) {
                    string? prompt = type switch {
                        ProductDropdownType.Fuel => "Set fixed fuel consumption",
                        ProductDropdownType.Ingredient => "Set fixed ingredient consumption",
                        ProductDropdownType.Product => "Set fixed production amount",
                        _ => null
                    };
                    if (prompt != null) {
                        ButtonEvent evt;
                        if (recipe.fixedBuildings == 0) {
                            evt = gui.BuildButton(prompt);
                        }
                        else {
                            using (gui.EnterRowWithHelpIcon("This will replace the other fixed amount in this row.")) {
                                gui.allocator = RectAllocator.RemainingRow;
                                evt = gui.BuildButton(prompt);
                            }
                        }
                        if (evt && gui.CloseDropdown()) {
                            recipe.RecordUndo().fixedBuildings = recipe.buildingCount <= 0 ? 1 : recipe.buildingCount;
                            switch (type) {
                                case ProductDropdownType.Fuel:
                                    recipe.fixedFuel = true;
                                    recipe.fixedIngredient = null;
                                    recipe.fixedProduct = null;
                                    break;
                                case ProductDropdownType.Ingredient:
                                    recipe.fixedFuel = false;
                                    recipe.fixedIngredient = goods;
                                    recipe.fixedProduct = null;
                                    break;
                                case ProductDropdownType.Product:
                                    recipe.fixedFuel = false;
                                    recipe.fixedIngredient = null;
                                    recipe.fixedProduct = goods;
                                    break;
                                default:
                                    break;
                            }
                            recipe.FocusFixedCountOnNextDraw();
                            targetGui.Rebuild();
                        }
                    }
                }

                if (recipe.fixedBuildings != 0
                    && ((type == ProductDropdownType.Fuel && recipe.fixedFuel)
                    || (type == ProductDropdownType.Ingredient && recipe.fixedIngredient == goods)
                    || (type == ProductDropdownType.Product && recipe.fixedProduct == goods))) {
                    string? prompt = type switch {
                        ProductDropdownType.Fuel => "Clear fixed fuel consumption",
                        ProductDropdownType.Ingredient => "Clear fixed ingredient consumption",
                        ProductDropdownType.Product => "Clear fixed production amount",
                        _ => null
                    };
                    if (prompt != null && gui.BuildButton(prompt) && gui.CloseDropdown()) {
                        recipe.RecordUndo().fixedBuildings = 0;
                    }
                    targetGui.Rebuild();
                }
            }
            #endregion

            if (goods is Item) {
                BuildBeltInserterInfo(gui, amount, recipe?.buildingCount ?? 0);
            }
        }
    }

    public override void SetSearchQuery(SearchQuery query) {
        _ = model.Search(query);
        bodyContent.Rebuild();
    }

    private void DrawDesiredProduct(ImGui gui, ProductionLink element) {
        gui.allocator = RectAllocator.Stretch;
        gui.spacing = 0f;
        SchemeColor iconColor = SchemeColor.Primary;

        if (element.flags.HasFlags(ProductionLink.Flags.LinkNotMatched)) {
            if (element.linkFlow > element.amount && CheckPossibleOverproducing(element)) {
                // Actual overproduction occurred for this product
                iconColor = SchemeColor.Magenta;
            }
            else {
                // There is not enough production (most likely none at all, otherwise the analyzer will have a deadlock)
                iconColor = SchemeColor.Error;
            }
        }

        ObjectTooltipOptions tooltipOptions = element.amount < 0 ? HintLocations.OnConsumingRecipes : HintLocations.OnProducingRecipes;
        if (element.LinkWarnings is IEnumerable<string> warnings) {
            tooltipOptions.DrawBelowHeader = gui => {
                foreach (string warning in warnings) {
                    gui.BuildText(warning, TextBlockDisplayStyle.ErrorText);
                }
            };
        }

        DisplayAmount amount = new(element.amount, element.goods.flowUnitOfMeasure);
        switch (gui.BuildFactorioObjectWithEditableAmount(element.goods, amount, ButtonDisplayStyle.ProductionTableScaled(iconColor), tooltipOptions: tooltipOptions)) {
            case GoodsWithAmountEvent.LeftButtonClick:
                OpenProductDropdown(gui, gui.lastRect, element.goods, element.amount, element,
                    element.amount < 0 ? ProductDropdownType.DesiredIngredient : ProductDropdownType.DesiredProduct, null, element.owner);
                break;
            case GoodsWithAmountEvent.RightButtonClick:
                DestroyLink(element);
                break;
            case GoodsWithAmountEvent.TextEditing when amount.Value != 0:
                element.RecordUndo().amount = amount.Value;
                break;
        }
    }

    public override void Rebuild(bool visualOnly = false) {
        flatHierarchyBuilder.SetData(model);
        base.Rebuild(visualOnly);
    }

    private void BuildGoodsIcon(ImGui gui, Goods? goods, ProductionLink? link, float amount, ProductDropdownType dropdownType,
        RecipeRow? recipe, ProductionTable context, ObjectTooltipOptions tooltipOptions, Goods[]? variants = null) {

        SchemeColor iconColor;

        if (link != null) {
            // The icon is part of a production link
            if ((link.flags & (ProductionLink.Flags.HasProductionAndConsumption | ProductionLink.Flags.LinkRecursiveNotMatched | ProductionLink.Flags.ChildNotMatched)) != ProductionLink.Flags.HasProductionAndConsumption) {
                // The link has production and consumption sides, but either the production and consumption is not matched, or 'child was not matched'
                iconColor = SchemeColor.Error;
            }
            // TODO (shpaass/yafc-ce/issues/269): refactor enum check into explicit instead of ordinal instructions
            else if (dropdownType >= ProductDropdownType.Product && CheckPossibleOverproducing(link)) {
                // Actual overproduction occurred in the recipe
                iconColor = SchemeColor.Magenta;
            }
            else if (link.owner != context) {
                // It is a foreign link (e.g. not part of the sub group)
                iconColor = SchemeColor.Secondary;
            }
            else {
                // Regular (nothing going on) linked icon
                iconColor = SchemeColor.Primary;
            }
        }
        else {
            // The icon is not part of a production link
            iconColor = goods.IsSourceResource() ? SchemeColor.Green : SchemeColor.None;
        }

        // TODO: See https://github.com/have-fun-was-taken/yafc-ce/issues/91
        //       and https://github.com/have-fun-was-taken/yafc-ce/pull/86#discussion_r1550377021
        SchemeColor textColor = flatHierarchyBuilder.nextRowTextColor;

        if (!flatHierarchyBuilder.nextRowIsHighlighted) {
            textColor = SchemeColor.None;
        }
        else if (recipe is { enabled: false }) {
            textColor = SchemeColor.BackgroundTextFaint;
        }

        GoodsWithAmountEvent evt;
        DisplayAmount displayAmount = new(amount, goods?.flowUnitOfMeasure ?? UnitOfMeasure.None);

        if (link?.LinkWarnings is IEnumerable<string> warnings) {
            tooltipOptions.DrawBelowHeader += gui => {
                foreach (string warning in warnings) {
                    gui.BuildText(warning, TextBlockDisplayStyle.ErrorText);
                }
            };
        }

        if (recipe != null && recipe.fixedBuildings > 0 && recipe.hierarchyEnabled
            && ((dropdownType == ProductDropdownType.Fuel && recipe.fixedFuel)
            || (dropdownType == ProductDropdownType.Ingredient && recipe.fixedIngredient == goods)
            || (dropdownType == ProductDropdownType.Product && recipe.fixedProduct == goods))) {

            evt = gui.BuildFactorioObjectWithEditableAmount(goods, displayAmount, ButtonDisplayStyle.ProductionTableScaled(iconColor), tooltipOptions: tooltipOptions,
                setKeyboardFocus: recipe.ShouldFocusFixedCountThisTime());
        }
        else {
            evt = (GoodsWithAmountEvent)gui.BuildFactorioObjectWithAmount(goods, displayAmount, ButtonDisplayStyle.ProductionTableScaled(iconColor),
                TextBlockDisplayStyle.Centered with { Color = textColor }, tooltipOptions: tooltipOptions);
        }

        switch (evt) {
            case GoodsWithAmountEvent.LeftButtonClick when goods is not null:
                OpenProductDropdown(gui, gui.lastRect, goods, amount, link, dropdownType, recipe, context, variants);
                break;
            case GoodsWithAmountEvent.RightButtonClick when goods is not null and not { isLinkable: false } && (link is null || link.owner != context):
                CreateLink(context, goods);
                break;
            case GoodsWithAmountEvent.RightButtonClick when link?.amount == 0 && link.owner == context:
                DestroyLink(link);
                break;
            case GoodsWithAmountEvent.TextEditing when displayAmount.Value >= 0:
                // The amount is always stored in fixedBuildings. Scale it to match the requested change to this item.
                recipe!.RecordUndo().fixedBuildings *= displayAmount.Value / amount;
                break;
        }
    }

    /// <summary>
    /// Checks some criteria that are necessary but not sufficient to consider something overproduced.
    /// </summary>
    private static bool CheckPossibleOverproducing(ProductionLink link) => link.algorithm == LinkAlgorithm.AllowOverProduction && link.flags.HasFlag(ProductionLink.Flags.LinkNotMatched);

    /// <param name="isForSummary">If <see langword="true"/>, this call is for a summary box, at the top of a root-level or nested table.
    /// If <see langword="false"/>, this call is for collapsed recipe row.</param>
    /// <param name="initializeDrawArea">If not <see langword="null"/>, this will be called before drawing the first element. This method may choose not to draw
    /// some or all of a table's extra products, and this lets the caller suppress the surrounding UI elements if no product end up being drawn.</param>
    private void BuildTableProducts(ImGui gui, ProductionTable table, ProductionTable context, ref ImGuiUtils.InlineGridBuilder grid,
        bool isForSummary, Action<ImGui>? initializeDrawArea = null) {

        var flow = table.flow;
        int firstProduct = Array.BinarySearch(flow, new ProductionTableFlow(Database.voidEnergy, 1e-9f, null), model);

        if (firstProduct < 0) {
            firstProduct = ~firstProduct;
        }

        for (int i = firstProduct; i < flow.Length; i++) {
            float amt = flow[i].amount;

            if (isForSummary) {
                amt -= flow[i].link?.amount ?? 0;
            }

            if (amt <= 0f) {
                continue;
            }

            initializeDrawArea?.Invoke(gui);
            initializeDrawArea = null;

            grid.Next();
            BuildGoodsIcon(gui, flow[i].goods, flow[i].link, amt, ProductDropdownType.Product, null, context, HintLocations.OnConsumingRecipes);
        }
    }

    private static void FillRecipeList(ProductionTable table, List<RecipeRow> list) {
        foreach (var recipe in table.recipes) {
            list.Add(recipe);

            if (recipe.subgroup != null) {
                FillRecipeList(recipe.subgroup, list);
            }
        }
    }

    private static void FillLinkList(ProductionTable table, List<ProductionLink> list) {
        list.AddRange(table.links);
        foreach (var recipe in table.recipes) {
            if (recipe.subgroup != null) {
                FillLinkList(recipe.subgroup, list);
            }
        }
    }

    private List<RecipeRow> GetRecipesRecursive() {
        List<RecipeRow> list = [];
        FillRecipeList(model, list);
        return list;
    }

    private static List<RecipeRow> GetRecipesRecursive(RecipeRow recipeRoot) {
        List<RecipeRow> list = [recipeRoot];

        if (recipeRoot.subgroup != null) {
            FillRecipeList(recipeRoot.subgroup, list);
        }

        return list;
    }

    private void BuildShoppingList(RecipeRow? recipeRoot) => ShoppingListScreen.Show(recipeRoot == null ? GetRecipesRecursive() : GetRecipesRecursive(recipeRoot));

    private static void BuildBeltInserterInfo(ImGui gui, float amount, float buildingCount) {
        var preferences = Project.current.preferences;
        var belt = preferences.defaultBelt;
        var inserter = preferences.defaultInserter;

        if (belt == null || inserter == null) {
            return;
        }

        float beltCount = amount / belt.beltItemsPerSecond;
        float buildingsPerHalfBelt = belt.beltItemsPerSecond * buildingCount / (amount * 2f);
        bool click = false;

        using (gui.EnterRow()) {
            click |= gui.BuildFactorioObjectButton(belt, ButtonDisplayStyle.Default) == Click.Left;
            gui.BuildText(DataUtils.FormatAmount(beltCount, UnitOfMeasure.None));

            if (buildingsPerHalfBelt > 0f) {
                gui.BuildText("(Buildings per half belt: " + DataUtils.FormatAmount(buildingsPerHalfBelt, UnitOfMeasure.None) + ")");
            }
        }

        using (gui.EnterRow()) {
            int capacity = preferences.inserterCapacity;
            float inserterBase = inserter.inserterSwingTime * amount / capacity;
            click |= gui.BuildFactorioObjectButton(inserter, ButtonDisplayStyle.Default) == Click.Left;
            string text = DataUtils.FormatAmount(inserterBase, UnitOfMeasure.None);

            if (buildingCount > 1) {
                text += " (" + DataUtils.FormatAmount(inserterBase / buildingCount, UnitOfMeasure.None) + "/building)";
            }

            gui.BuildText(text);

            if (capacity > 1) {
                float withBeltSwingTime = inserter.inserterSwingTime + (2f * (capacity - 1.5f) / belt.beltItemsPerSecond);
                float inserterToBelt = amount * withBeltSwingTime / capacity;
                click |= gui.BuildFactorioObjectButton(belt, ButtonDisplayStyle.Default) == Click.Left;
                gui.AllocateSpacing(-1.5f);
                click |= gui.BuildFactorioObjectButton(inserter, ButtonDisplayStyle.Default) == Click.Left;
                text = DataUtils.FormatAmount(inserterToBelt, UnitOfMeasure.None, "~");

                if (buildingCount > 1) {
                    text += " (" + DataUtils.FormatAmount(inserterToBelt / buildingCount, UnitOfMeasure.None) + "/b)";
                }

                gui.BuildText(text);
            }
        }

        if (click && gui.CloseDropdown()) {
            PreferencesScreen.ShowProgression();
        }
    }

    private void BuildTableIngredients(ImGui gui, ProductionTable table, ProductionTable context, ref ImGuiUtils.InlineGridBuilder grid) {
        foreach (var flow in table.flow) {
            if (flow.amount >= 0f) {
                break;
            }

            grid.Next();
            BuildGoodsIcon(gui, flow.goods, flow.link, -flow.amount, ProductDropdownType.Ingredient, null, context, HintLocations.OnProducingRecipes);
        }
    }

    private static void DrawRecipeTagSelect(ImGui gui, RecipeRow recipe) {
        using (gui.EnterRow()) {
            for (int i = 0; i < tagIcons.Length; i++) {
                var (icon, color) = tagIcons[i];
                bool selected = i == recipe.tag;
                gui.BuildIcon(icon, color: selected ? SchemeColor.Background : color);

                if (selected) {
                    gui.DrawRectangle(gui.lastRect, color);
                }
                else {
                    var evt = gui.BuildButton(gui.lastRect, SchemeColor.None, SchemeColor.BackgroundAlt, SchemeColor.BackgroundAlt);

                    if (evt) {
                        recipe.RecordUndo(true).tag = i;
                    }
                }
            }
        }
    }

    protected override void BuildHeader(ImGui gui) {
        base.BuildHeader(gui);
        flatHierarchyBuilder.BuildHeader(gui);
    }

    protected override void BuildPageTooltip(ImGui gui, ProductionTable contents) {
        foreach (var link in contents.links) {
            if (link.amount != 0f) {
                using (gui.EnterRow()) {
                    gui.BuildFactorioObjectIcon(link.goods);
                    using (gui.EnterGroup(default, RectAllocator.LeftAlign, spacing: 0)) {
                        gui.BuildText(link.goods.locName);
                        gui.BuildText(DataUtils.FormatAmount(link.amount, link.goods.flowUnitOfMeasure));
                    }
                }
            }
        }

        foreach (var row in contents.recipes) {
            if (row.fixedBuildings != 0 && row.entity != null) {
                using (gui.EnterRow()) {
                    gui.BuildFactorioObjectIcon(row.recipe);
                    using (gui.EnterGroup(default, RectAllocator.LeftAlign, spacing: 0)) {
                        gui.BuildText(row.recipe.locName);
                        gui.BuildText(row.entity.target.locName + ": " + DataUtils.FormatAmount(row.fixedBuildings, UnitOfMeasure.None));
                    }
                }
            }

            if (row.subgroup != null) {
                BuildPageTooltip(gui, row.subgroup);
            }
        }
    }

    private static readonly Dictionary<WarningFlags, string> WarningsMeaning = new Dictionary<WarningFlags, string>
    {
        {WarningFlags.DeadlockCandidate, "Contains recursive links that cannot be matched. No solution exists."},
        {WarningFlags.OverproductionRequired, "This model cannot be solved exactly, it requires some overproduction. You can allow overproduction for any link. " +
            "This recipe contains one of the possible candidates."},
        {WarningFlags.EntityNotSpecified, "Crafter not specified. Solution is inaccurate." },
        {WarningFlags.FuelNotSpecified, "Fuel not specified. Solution is inaccurate." },
        {WarningFlags.FuelWithTemperatureNotLinked, "This recipe uses fuel with temperature. Should link with producing entity to determine temperature."},
        {WarningFlags.FuelTemperatureExceedsMaximum, "Fluid temperature is higher than generator maximum. Some energy is wasted."},
        {WarningFlags.FuelDoesNotProvideEnergy, "This fuel cannot provide any energy to this building. The building won't work."},
        {WarningFlags.FuelUsageInputLimited, "This building has max fuel consumption. The rate at which it works is limited by it."},
        {WarningFlags.TemperatureForIngredientNotMatch, "This recipe does care about ingredient temperature, and the temperature range does not match"},
        {WarningFlags.ReactorsNeighborsFromPrefs, "Assumes reactor formation from preferences"},
        {WarningFlags.AssumesNauvisSolarRatio, "Energy production values assumes Nauvis solar ration (70% power output). Don't forget accumulators."},
        {WarningFlags.ExceedsBuiltCount, "This recipe requires more buildings than are currently built."},
        {WarningFlags.AsteroidCollectionNotModelled, "The speed of asteroid collectors depends heavily on location and travel speed. " +
            "It also depends on the distance between adjacent collectors. These dependencies are not modeled. Expect widely varied performance."},
        {WarningFlags.AssumesFulgoraAndModel, "Energy production values assume Fulgoran storms and attractors in a square grid.\n" +
            "The accumulator estimate tries to store 10% of the energy captured by the attractors."},
    };

    private static readonly (Icon icon, SchemeColor color)[] tagIcons = [
        (Icon.Empty, SchemeColor.BackgroundTextFaint),
        (Icon.Check, SchemeColor.Green),
        (Icon.Warning, SchemeColor.Secondary),
        (Icon.Error, SchemeColor.Error),
        (Icon.Edit, SchemeColor.Primary),
        (Icon.Help, SchemeColor.BackgroundText),
        (Icon.Time, SchemeColor.BackgroundText),
        (Icon.DarkMode, SchemeColor.BackgroundText),
        (Icon.Settings, SchemeColor.BackgroundText),
    ];

    protected override void BuildContent(ImGui gui) {
        if (model == null) {
            return;
        }

        BuildSummary(gui, model);
        gui.AllocateSpacing();
        flatHierarchyBuilder.Build(gui);
        gui.SetMinWidth(flatHierarchyBuilder.width);
    }

    private static void AddDesiredProductAtLevel(ProductionTable table) => SelectMultiObjectPanel.Select(
        Database.goods.all.Except(table.linkMap.Where(p => p.Value.amount != 0).Select(p => p.Key)).Where(g => g.isLinkable), "Add desired product", product => {
            if (table.linkMap.TryGetValue(product, out var existing)) {
                if (existing.amount != 0) {
                    return;
                }

                existing.RecordUndo().amount = 1f;
            }
            else {
                table.RecordUndo().links.Add(new ProductionLink(table, product) { amount = 1f });
            }
        });

    private void BuildSummary(ImGui gui, ProductionTable table) {
        bool isRoot = table == model;
        if (!isRoot && !table.containsDesiredProducts) {
            return;
        }

        int elementsPerRow = MathUtils.Floor((flatHierarchyBuilder.width - 2f) / 4f);
        gui.spacing = 1f;
        Padding pad = new Padding(1f, 0.2f);
        using (gui.EnterGroup(pad)) {
            gui.BuildText("Desired products and amounts (Use negative for input goal):");
            using var grid = gui.EnterInlineGrid(3f, 1f, elementsPerRow);
            foreach (var link in table.links.ToList()) {
                if (link.amount != 0f) {
                    grid.Next();
                    DrawDesiredProduct(gui, link);
                }
            }

            grid.Next();
            if (gui.BuildButton(Icon.Plus, SchemeColor.Primary, SchemeColor.PrimaryAlt, size: 2.5f)) {
                AddDesiredProductAtLevel(table);
            }
        }

        if (gui.isBuilding) {
            gui.DrawRectangle(gui.lastRect, SchemeColor.Background, RectangleBorder.Thin);
        }

        if (table.flow.Length > 0 && table.flow[0].amount < 0) {
            using (gui.EnterGroup(pad)) {
                gui.BuildText(isRoot ? "Summary ingredients:" : "Import ingredients:");
                var grid = gui.EnterInlineGrid(3f, 1f, elementsPerRow);
                BuildTableIngredients(gui, table, table, ref grid);
                grid.Dispose();
            }

            if (gui.isBuilding) {
                gui.DrawRectangle(gui.lastRect, SchemeColor.Background, RectangleBorder.Thin);
            }
        }

        if (table.flow.Length > 0 && table.flow[^1].amount > 0) {
            ImGui.Context? context = null;
            ImGuiUtils.InlineGridBuilder grid = default;
            void initializeGrid(ImGui gui) {
                context = gui.EnterGroup(pad);
                gui.BuildText(isRoot ? "Extra products:" : "Export products:");
                grid = gui.EnterInlineGrid(3f, 1f, elementsPerRow);
            }

            BuildTableProducts(gui, table, table, ref grid, true, initializeGrid);

            if (context != null) {
                grid.Dispose();
                context.Value.Dispose();

                if (gui.isBuilding) {
                    gui.DrawRectangle(gui.lastRect, SchemeColor.Background, RectangleBorder.Thin);
                }
            }
        }
    }
}
