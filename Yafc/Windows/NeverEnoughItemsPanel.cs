﻿using System;
using System.Collections.Generic;
using Yafc.I18n;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

public class NeverEnoughItemsPanel : PseudoScreen, IComparer<NeverEnoughItemsPanel.RecipeEntry> {
    private static readonly NeverEnoughItemsPanel Instance = new NeverEnoughItemsPanel();
    private Goods current = null!; // null-forgiving: Set by Show.
    private Goods? changing;
    private float currentFlow;
    private EntryStatus showRecipesRange = EntryStatus.Normal;
    private readonly List<Goods> recent = [];
    private bool atCurrentMilestones;

    private readonly ScrollArea productionList;
    private readonly ScrollArea usageList;

    private enum EntryStatus {
        NotAccessible,
        NotAccessibleWithCurrentMilestones,
        Special,
        Normal,
        Useful
    }

    private readonly struct RecipeEntry {
        public readonly Recipe recipe;
        public readonly float recipeFlow;
        public readonly float flow;
        public readonly float specificEfficiency;
        public readonly EntryStatus entryStatus;

        public RecipeEntry(Recipe recipe, bool isProduction, Goods currentItem, bool atCurrentMilestones) {
            this.recipe = recipe;
            float amount = isProduction ? recipe.GetProductionPerRecipe(currentItem) : recipe.GetConsumptionPerRecipe(currentItem);
            recipeFlow = recipe.ApproximateFlow(atCurrentMilestones);
            flow = recipeFlow * amount;
            specificEfficiency = isProduction ? recipe.Cost() / amount : 0f;
            if (!recipe.IsAccessible()) {
                entryStatus = EntryStatus.NotAccessible;
            }
            else if (!recipe.IsAccessibleWithCurrentMilestones()) {
                entryStatus = EntryStatus.NotAccessibleWithCurrentMilestones;
            }
            else {
                float waste = recipe.RecipeWaste(atCurrentMilestones);
                if (recipe.specialType != FactorioObjectSpecialType.Normal && recipeFlow <= 0.01f) {
                    entryStatus = EntryStatus.Special;
                }
                else if (waste > 0f) {
                    entryStatus = EntryStatus.Normal;
                }
                else {
                    entryStatus = EntryStatus.Useful;
                }
            }
        }
    }

    private RecipeEntry[] productions = [];
    private RecipeEntry[] usages = [];

    private NeverEnoughItemsPanel() : base(76f) {
        productionList = new ScrollArea(40f, BuildItemProduction, new Padding(0.5f));
        usageList = new ScrollArea(40f, BuildItemUsages, new Padding(0.5f));
    }

    /// <summary>
    /// Call to make sure that any recent setting changes (e.g. object accessibility) are reflected in the NEIE display.
    /// It is only necessary to call this from screens that could be displayed on top of the NEIE display.
    /// </summary>
    public static void Refresh() {
        var item = Instance.current;
        Instance.current = null!; // null-forgiving: We immediately reset this.
        Instance.SetItem(item);
    }

    private void SetItem(Goods current) {
        if (current == this.current) {
            return;
        }

        _ = recent.Remove(current);
        if (recent.Count > 18) {
            recent.RemoveAt(0);
        }

        if (this.current != null) {
            recent.Add(this.current);
        }

        currentFlow = current.ApproximateFlow(atCurrentMilestones);
        RecipeEntry[] refreshedProductions = new RecipeEntry[current.production.Length];
        for (int i = 0; i < current.production.Length; i++) {
            refreshedProductions[i] = new RecipeEntry(current.production[i], true, current, atCurrentMilestones);
        }
        Array.Sort(refreshedProductions, this);

        RecipeEntry[] refreshedUsages = new RecipeEntry[current.usages.Length];
        for (int i = 0; i < current.usages.Length; i++) {
            refreshedUsages[i] = new RecipeEntry(current.usages[i], false, current, atCurrentMilestones);
        }
        Array.Sort(refreshedUsages, this);

        this.current = current;
        productions = refreshedProductions;
        usages = refreshedUsages;

        Rebuild();
        productionList.Rebuild();
        usageList.Rebuild();
    }

    private void DrawIngredients(ImGui gui, Recipe recipe) {
        if (recipe.ingredients.Length > 8) {
            DrawTooManyThings(gui, recipe.ingredients, 8);
            return;
        }
        foreach (var ingredient in recipe.ingredients) {
            if (gui.BuildFactorioObjectWithAmount(ingredient.goods, ingredient.amount, ButtonDisplayStyle.NeieSmall) == Click.Left) {
                if (ingredient.variants != null) {
                    gui.ShowDropDown(imGui => imGui.BuildInlineObjectListAndButton(ingredient.variants, SetItem, new(LSs.NeieAcceptedVariants)));
                }
                else {
                    changing = ingredient.goods;
                }
            }
        }
    }

    private void DrawProducts(ImGui gui, Recipe recipe) {
        if (recipe.products.Length > 7) {
            DrawTooManyThings(gui, recipe.products, 7);
            return;
        }
        for (int i = recipe.products.Length - 1; i >= 0; i--) {
            var product = recipe.products[i];
            if (gui.BuildFactorioObjectWithAmount(product.goods, product.amount, ButtonDisplayStyle.NeieSmall) == Click.Left) {
                changing = product.goods;
            }
        }
    }

    private void DrawTooManyThings(ImGui gui, IEnumerable<IFactorioObjectWrapper> list, int maxElemCount) {
        using var grid = gui.EnterInlineGrid(3f, 0f, maxElemCount);
        foreach (var item in list) {
            grid.Next();
            if (gui.BuildFactorioObjectWithAmount(item.target, item.amount, ButtonDisplayStyle.NeieSmall) == Click.Left) {
                changing = item.target as Goods;
            }
        }
    }

    private void CheckChanging() {
        if (changing != null) {
            SetItem(changing);
            changing = null;
        }
    }

    private void DrawRecipeEntry(ImGui gui, RecipeEntry entry, bool production) {
        var textColor = SchemeColor.BackgroundText;
        var bgColor = SchemeColor.Background;
        bool isBuilding = gui.isBuilding;
        var recipe = entry.recipe;
        float waste = recipe.RecipeWaste(atCurrentMilestones);
        if (isBuilding) {
            if (entry.entryStatus == EntryStatus.NotAccessible) {
                bgColor = SchemeColor.None;
                textColor = SchemeColor.BackgroundTextFaint;
            }
            else if (entry.flow > 0f) {
                bgColor = SchemeColor.Secondary;
                textColor = SchemeColor.SecondaryText;
            }
        }
        using (gui.EnterGroup(new Padding(0.5f), production ? RectAllocator.LeftRow : RectAllocator.RightRow, textColor)) {
            using (gui.EnterFixedPositioning(4f, 0f, default)) {
                gui.allocator = RectAllocator.Stretch;
                _ = gui.BuildFactorioObjectButton(entry.recipe, ButtonDisplayStyle.NeieLarge);
                using (gui.EnterRow()) {
                    gui.BuildIcon(Icon.Time);
                    gui.BuildText(DataUtils.FormatAmount(entry.recipe.time, UnitOfMeasure.Second), TextBlockDisplayStyle.Centered);
                }
                float bh = CostAnalysis.GetBuildingHours(recipe, entry.recipeFlow);
                if (bh > 20) {
                    gui.BuildText(LSs.NeieBuildingHoursSuffix.L(DataUtils.FormatAmount(bh, UnitOfMeasure.None)), TextBlockDisplayStyle.Centered);

                    _ = gui.BuildButton(gui.lastRect, SchemeColor.None, SchemeColor.Grey)
                        .WithTooltip(gui, LSs.NeieBuildingHoursDescription);
                }
            }
            gui.AllocateSpacing();
            var textAlloc = production ? RectAllocator.LeftAlign : RectAllocator.RightAlign;
            gui.allocator = textAlloc;
            using (gui.EnterRow(0f, production ? RectAllocator.RightRow : RectAllocator.LeftRow)) {
                bool favorite = Project.current.preferences.favorites.Contains(entry.recipe);
                var iconRect = gui.AllocateRect(1f, 1f).Expand(0.25f);
                gui.DrawIcon(iconRect, favorite ? Icon.StarFull : Icon.StarEmpty, SchemeColor.BackgroundText);
                if (gui.BuildButton(iconRect, SchemeColor.None, SchemeColor.BackgroundAlt)) {
                    Project.current.preferences.ToggleFavorite(entry.recipe);
                }

                gui.allocator = textAlloc;
                gui.BuildText(recipe.locName, TextBlockDisplayStyle.WrappedText);
            }
            if (recipe.ingredients.Length + recipe.products.Length <= 8) {
                using (gui.EnterRow()) {
                    DrawIngredients(gui, entry.recipe);
                    gui.allocator = RectAllocator.RightRow;
                    DrawProducts(gui, entry.recipe);
                    if (recipe.products.Length < 3 && recipe.ingredients.Length < 5) {
                        gui.AllocateSpacing((3 - entry.recipe.products.Length) * 3f);
                    }
                    else if (recipe.products.Length < 3) {
                        gui.allocator = RectAllocator.RemainingRow;
                    }

                    gui.BuildIcon(Icon.ArrowRight, 3f);
                }
            }
            else {
                using (gui.EnterRow()) {
                    DrawIngredients(gui, entry.recipe);
                }

                using (gui.EnterRow()) {
                    gui.BuildIcon(Icon.ArrowDownRight, 3f);
                    gui.allocator = RectAllocator.RightRow;
                    DrawProducts(gui, entry.recipe);
                }
            }
        }

        if (isBuilding) {
            var rect = gui.lastRect;
            if (entry.flow > 0f) {
                float percentFlow = MathUtils.Clamp(entry.flow / currentFlow, 0f, 1f);
                rect.Width *= percentFlow;
                gui.DrawRectangle(rect, SchemeColor.Primary);
            }
            else if (waste <= 0f) {
                bgColor = SchemeColor.Secondary;
            }
            else {
                rect.Width *= (1f - waste);
                gui.DrawRectangle(rect, SchemeColor.Secondary);
            }
            gui.DrawRectangle(gui.lastRect, bgColor);
        }
    }

    private void DrawEntryFooter(ImGui gui, bool production) {
        if (!production && current.fuelFor.Length > 0) {
            using (gui.EnterGroup(new Padding(0.5f), RectAllocator.LeftAlign)) {
                gui.BuildText(current.fuelValue > 0f ? LSs.FuelValueCanBeUsed.L(DataUtils.FormatAmount(current.fuelValue, UnitOfMeasure.Megajoule)) : LSs.FuelValueZeroCanBeUsed);
                using var grid = gui.EnterInlineGrid(3f);
                foreach (var fuelUsage in current.fuelFor) {
                    grid.Next();
                    _ = gui.BuildFactorioObjectButton(fuelUsage, ButtonDisplayStyle.NeieSmall);
                }
            }
            if (gui.isBuilding) {
                gui.DrawRectangle(gui.lastRect, SchemeColor.Primary);
            }
        }
    }

    private void ChangeShowStatus(EntryStatus status) {
        showRecipesRange = status;
        Rebuild();
        productionList.Rebuild();
        usageList.Rebuild();
    }

    private void DrawEntryList(ImGui gui, ReadOnlySpan<RecipeEntry> entries, bool production) {
        bool footerDrawn = false;
        var prevEntryStatus = EntryStatus.Normal;
        FactorioObject? prevLatestMilestone = null;
        foreach (var entry in entries) {
            var status = entry.entryStatus;

            if (status < showRecipesRange) {
                DrawEntryFooter(gui, production);
                footerDrawn = true;
                gui.BuildText(entry.entryStatus == EntryStatus.Special ? LSs.NeieShowSpecialRecipes :
                    entry.entryStatus == EntryStatus.NotAccessibleWithCurrentMilestones ? LSs.NeieShowLockedRecipes :
                    LSs.NeieShowInaccessibleRecipes, TextBlockDisplayStyle.WrappedText);
                if (gui.BuildButton(LSs.NeieShowMoreRecipes)) {
                    ChangeShowStatus(status);
                }

                break;
            }

            if (status < prevEntryStatus) {
                prevEntryStatus = status;
                using (gui.EnterRow()) {
                    gui.BuildText(status == EntryStatus.Special ? LSs.NeieSpecialRecipes :
                        status == EntryStatus.NotAccessibleWithCurrentMilestones ? LSs.NeieLockedRecipes : LSs.NeieInaccessibleRecipes);

                    if (gui.BuildLink(LSs.NeieHideRecipes)) {
                        ChangeShowStatus(status + 1);
                    }
                }
            }

            if (status == EntryStatus.NotAccessibleWithCurrentMilestones) {
                var latest = Milestones.Instance.GetHighest(entry.recipe, false);
                if (latest != prevLatestMilestone) {
                    _ = gui.BuildFactorioObjectButtonWithText(latest, iconDisplayStyle: new(3, MilestoneDisplay.None, false));
                    prevLatestMilestone = latest;
                }
            }

            if (gui.ShouldBuildGroup(entry.recipe, out var group)) {
                DrawRecipeEntry(gui, entry, production);
                group.Complete();
            }
        }
        if (!footerDrawn) {
            DrawEntryFooter(gui, production);
        }

        CheckChanging();
    }

    private void BuildItemProduction(ImGui gui) => DrawEntryList(gui, productions, true);

    private void BuildItemUsages(ImGui gui) => DrawEntryList(gui, usages, false);

    public override void Build(ImGui gui) {
        BuildHeader(gui, LSs.NeieHeader);
        using (gui.EnterRow()) {
            if (recent.Count == 0) {
                _ = gui.AllocateRect(0f, 3f);
            }

            for (int i = recent.Count - 1; i >= 0; i--) {
                var elem = recent[i];
                if (gui.BuildFactorioObjectButton(elem, ButtonDisplayStyle.NeieSmall) == Click.Left) {
                    changing = elem;
                }
            }
        }
        using (gui.EnterGroup(new Padding(0.5f), RectAllocator.LeftRow)) {
            gui.spacing = 0.2f;
            gui.BuildFactorioObjectIcon(current, ButtonDisplayStyle.NeieSmall);
            gui.BuildText(current.locName, Font.subheader);
            gui.allocator = RectAllocator.RightAlign;
            gui.BuildText(CostAnalysis.GetDisplayCost(current));
            string? amount = CostAnalysis.Instance.GetItemAmount(current);
            if (amount != null) {
                gui.BuildText(amount, TextBlockDisplayStyle.WrappedText);
            }
        }

        if (gui.BuildFactorioObjectButtonBackground(gui.lastRect, current, SchemeColor.Grey) == Click.Left) {
            SelectSingleObjectPanel.Select(Database.goods.explorable, new(LSs.SelectItem), SetItem);
        }

        using (var split = gui.EnterHorizontalSplit(2)) {
            split.Next();
            gui.BuildText(LSs.NeieProduction, Font.subheader);
            productionList.Build(gui);
            split.Next();
            gui.BuildText(LSs.NeieUsage, Font.subheader);
            usageList.Build(gui);
        }
        CheckChanging();
        using (gui.EnterRow()) {
            if (gui.BuildLink(LSs.NeieColoredBarsLink)) {
                MessageBox.Show(LSs.NeieHowToReadColoredBars,
                    LSs.NeieColoredBarsDescription, LSs.Ok);
            }
            if (gui.BuildCheckBox(LSs.NeieCurrentMilestonesCheckbox, atCurrentMilestones, out atCurrentMilestones, allocator: RectAllocator.RightRow)) {
                Refresh();
            }
        }
    }

    public static void Show(Goods goods) {
        // This call handles any updates required by milestone changes. The milestones window can't
        // easily handle that since the setting updates happen after the milestones screens are closed.
        Refresh();

        if (Instance.opened) {
            Instance.changing = goods;
            return;
        }
        Instance.SetItem(goods);
        _ = MainScreen.Instance.ShowPseudoScreen(Instance);
    }
    int IComparer<RecipeEntry>.Compare(RecipeEntry x, RecipeEntry y) {
        if (x.entryStatus != y.entryStatus) {
            return y.entryStatus - x.entryStatus;
        }

        if (x.entryStatus == EntryStatus.NotAccessibleWithCurrentMilestones) {
            var xMilestone = DataUtils.GetMilestoneOrder(x.recipe.id);
            var yMilestone = DataUtils.GetMilestoneOrder(y.recipe.id);
            if (xMilestone != yMilestone) {
                return xMilestone.CompareTo(yMilestone);
            }
        }
        if (x.flow != y.flow) {
            return y.flow.CompareTo(x.flow);
        }

        return x.recipe.RecipeWaste(atCurrentMilestones).CompareTo(y.recipe.RecipeWaste(atCurrentMilestones));
    }
}
