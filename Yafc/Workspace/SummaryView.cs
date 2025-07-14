﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Yafc.I18n;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

public class SummaryView : ProjectPageView<Summary> {
    ///<summary>Size of the main contents (in <see cref="scrollArea"/>)</summary>
    private Vector2 contentSize;
    /// <summary>Height of each row</summary>
    private float rowHeight;
    /// <summary>Extra <see cref="rowHeight"/> that is added by the DataGrid</summary>
    private static readonly float extraRowHeight = DataGrid<ProjectPage>.innerPadding.top + DataGrid<ProjectPage>.innerPadding.bottom;

    /// <summary>Some padding to have the contents of the first column not 'stick' to the rest of the UI</summary>
    private readonly Padding FirstColumnPadding = new Padding(1f, 1.5f, 0, 0);
    private float firstColumnWidth;

    private class SummaryScrollArea(GuiBuilder builder) : ScrollArea(DefaultHeight, builder, horizontal: true, vertical: false) {
        private static readonly float DefaultHeight = 10;

        public void Build(ImGui gui, float contentHeight) =>
            base.Build(gui, contentHeight != 0 ? contentHeight + ScrollbarSize : DefaultHeight);
    }

    private sealed class SummaryDataColumn(SummaryView view) : TextDataColumn<ProjectPage>(LSs.SummaryColumnLinked, float.MaxValue) {
        public override void BuildElement(ImGui gui, ProjectPage page) {
            if (page?.contentType != typeof(ProductionTable)) {
                return;
            }

            ProductionTable table = (ProductionTable)page.content;
            using var grid = gui.EnterInlineGrid(ElementWidth, ElementSpacing);
            foreach ((string name, GoodDetails details, bool enoughProduced) in view.GoodsToDisplay()) {
                ProductionLink? link = table.links.Find(x => x.goods.QualityName() == name);
                grid.Next();
                bool isDrawn = false;

                if (link != null) {
                    if (link.amount != 0f) {
                        DrawProvideProduct(gui, link, page, details, enoughProduced);
                        isDrawn = true;
                    }
                }
                else {
                    if (Array.Exists(table.flow, x => x.goods.QualityName() == name)) {
                        ProductionTableFlow flow = Array.Find(table.flow, x => x.goods.QualityName() == name);

                        if (Math.Abs(flow.amount) > Epsilon) {

                            DrawRequestProduct(gui, flow, enoughProduced);
                            isDrawn = true;
                        }
                    }
                }
                if (!isDrawn) {
                    // Reserve empty space to prevent 'compressing' empty rows, causing mismatches with the fake rows of firstColum
                    _ = gui.AllocateRect(0f, view.rowHeight);
                }

            }
        }

        private static void DrawProvideProduct(ImGui gui, ProductionLink element, ProjectPage page, GoodDetails goodInfo, bool enoughProduced) {
            SchemeColor iconColor;

            if (element.amount > 0 && enoughProduced) {
                // Production matches consumption
                iconColor = SchemeColor.Primary;
            }
            else if (element.amount < 0 && goodInfo.extraProduced == -element.amount) {
                // Extra produced matches input goal
                iconColor = SchemeColor.Primary;
            }
            else {
                // Production and consumption does not match
                iconColor = SchemeColor.Error;
            }

            gui.allocator = RectAllocator.Stretch;
            gui.spacing = 0f;
            DisplayAmount amount = new(element.amount, element.goods.target.flowUnitOfMeasure);
            GoodsWithAmountEvent evt = gui.BuildFactorioObjectWithEditableAmount(element.goods, amount, ButtonDisplayStyle.ProductionTableScaled(iconColor));

            if (evt == GoodsWithAmountEvent.TextEditing && amount.Value != 0) {
                _ = SetProviderAmount(element, page, amount.Value);
            }
            else if (evt == GoodsWithAmountEvent.LeftButtonClick) {
                _ = SetProviderAmount(element, page, YafcRounding(goodInfo.sum));
            }
        }
        private static void DrawRequestProduct(ImGui gui, ProductionTableFlow flow, bool enoughExtraProduced) {
            SchemeColor iconColor;
            if (flow.amount > Epsilon) {
                if (!enoughExtraProduced) {
                    // Not enough in extra production, while being depended on
                    iconColor = SchemeColor.Error;
                }
                else {
                    // Enough extra product produced
                    iconColor = SchemeColor.Green;
                }
            }
            else {
                // Flow amount negative or (almost, due to rounding errors) zero, just a regular request (nothing to indicate)
                iconColor = SchemeColor.None;
            }

            gui.allocator = RectAllocator.Stretch;
            gui.spacing = 0f;
            _ = gui.BuildFactorioObjectWithAmount(flow.goods, new(-flow.amount, flow.goods.target.flowUnitOfMeasure), ButtonDisplayStyle.ProductionTableScaled(iconColor));
        }

        internal static Task SetProviderAmount(ProductionLink element, ProjectPage page, float newAmount) {
            element.RecordUndo().amount = newAmount;
            return page.RunSolveJob();
        }
    }

    private static readonly float Epsilon = 1e-5f;
    private static readonly float ElementWidth = 3;
    private static readonly float ElementSpacing = 1;
    private static readonly float FirstColumnIconSize = 1.5f;

    private struct GoodDetails {
        public float totalProvided;
        public float totalNeeded;
        public float extraProduced;
        public float sum;
    }

    private Project project;
    private SearchQuery searchQuery;

    private readonly SummaryScrollArea scrollArea;
    private readonly SummaryDataColumn goodsColumn;
    private readonly DataGrid<ProjectPage> mainGrid;
    ///<summary>Separate panel to contain the first colum (page names)</summary>
    public readonly ImGui firstColumContent;

    private Dictionary<string, GoodDetails> allGoods = [];

    private IEnumerable<(string name, GoodDetails details, bool enoughProduced)> GoodsToDisplay() {
        foreach (KeyValuePair<string, GoodDetails> goodInfo in allGoods) {
            if (!searchQuery.Match(goodInfo.Key)) {
                continue;
            }

            float amountAvailable = YafcRounding((goodInfo.Value.totalProvided > 0 ? goodInfo.Value.totalProvided : 0) + goodInfo.Value.extraProduced);
            float amountNeeded = YafcRounding((goodInfo.Value.totalProvided < 0 ? -goodInfo.Value.totalProvided : 0) + goodInfo.Value.totalNeeded);

            if (model == null || (model.showOnlyIssues && (Math.Abs(amountAvailable - amountNeeded) < Epsilon || amountNeeded == 0))) {
                continue;
            }

            bool enoughProduced = amountAvailable >= amountNeeded;

            yield return (goodInfo.Key, goodInfo.Value, enoughProduced);
        }
    }

    private IEnumerable<(string name, GoodDetails details, float excess)> GoodsToBalance() {
        foreach (KeyValuePair<string, GoodDetails> goodInfo in allGoods) {
            float amountAvailable = YafcRounding((goodInfo.Value.totalProvided > 0 ? goodInfo.Value.totalProvided : 0) + goodInfo.Value.extraProduced);
            float amountNeeded = YafcRounding((goodInfo.Value.totalProvided < 0 ? -goodInfo.Value.totalProvided : 0) + goodInfo.Value.totalNeeded);

            if (Math.Abs(amountAvailable - amountNeeded) < Epsilon || amountNeeded == 0) {
                continue;
            }

            yield return (goodInfo.Key, goodInfo.Value, amountAvailable - amountNeeded);
        }
    }

    public SummaryView(Project project) {
        goodsColumn = new SummaryDataColumn(this);
        scrollArea = new SummaryScrollArea(BuildScrollArea);
        mainGrid = new DataGrid<ProjectPage>([goodsColumn]);
        firstColumContent = new ImGui(BuildFirstColumn, default, RectAllocator.LeftAlign);

        SetProject(project);
    }

    [MemberNotNull(nameof(project))]
    public void SetProject(Project project) {
        if (this.project != null) {
            this.project.metaInfoChanged -= Recalculate;

            foreach (ProjectPage page in this.project.pages) {
                page.contentChanged -= Recalculate;
            }
        }

        this.project = project;
        project.metaInfoChanged += Recalculate;

        foreach (ProjectPage page in project.pages) {
            page.contentChanged += Recalculate;
        }
    }

    protected override void BuildPageTooltip(ImGui gui, Summary contents) {
    }

    protected override void BuildHeader(ImGui gui) {
        base.BuildHeader(gui);

        gui.allocator = RectAllocator.Center;
        gui.BuildText(LSs.SummaryHeader, new TextBlockDisplayStyle(Font.header, Alignment: RectAlignment.Middle));
        gui.allocator = RectAllocator.LeftAlign;
    }

    private float CalculateFirstColumWidth(ImGui gui) {
        // At the least the icons should fit
        float width = FirstColumnIconSize;

        foreach (Guid displayPage in project.displayPages) {
            ProjectPage? page = project.FindPage(displayPage);

            if (page?.contentType != typeof(ProductionTable)) {
                continue;
            }

            Vector2 textSize = gui.GetTextDimensions(out _, page.name);
            width = MathF.Max(width, textSize.X);
        }

        // Include padding to perfectly match
        return width + FirstColumnPadding.left + FirstColumnPadding.right;
    }

    private void BuildFirstColumn(ImGui gui) {
        foreach (Guid displayPage in project.displayPages) {
            ProjectPage? page = project.FindPage(displayPage);
            if (page?.contentType != typeof(ProductionTable)) {
                continue;
            }

            using (gui.EnterFixedPositioning(firstColumnWidth, rowHeight + extraRowHeight, FirstColumnPadding)) {
                gui.allocator = RectAllocator.LeftAlign;
                gui.spacing = 0.2f;
                if (page.icon != null) {
                    gui.BuildIcon(page.icon.icon, FirstColumnIconSize);
                }
                else {
                    _ = gui.AllocateRect(0f, FirstColumnIconSize);
                }

                gui.BuildText(page.name);
            }
        }
    }

    protected override async void BuildContent(ImGui gui) {
        using (gui.EnterRow()) {
            _ = gui.AllocateRect(0, 2); // Increase row height to 2, for vertical centering.

            if (gui.BuildCheckBox(LSs.SummaryOnlyShowIssues, model?.showOnlyIssues ?? false, out bool newValue)) {
                model!.showOnlyIssues = newValue; // null-forgiving: when model is null, the page is no longer being displayed, so no clicks can happen.
                Recalculate();
            }

            using (gui.EnterRowWithHelpIcon(LSs.SummaryAutoBalanceHint, false)) {
                if (gui.BuildButton(LSs.SummaryAutoBalance)) {
                    await AutoBalance();
                }
            }
        }
        // gui.AllocateSpacing();

        if (gui.isBuilding) {
            firstColumnWidth = CalculateFirstColumWidth(gui);
            rowHeight = ButtonDisplayStyle.ProductionTableUnscaled.Size + gui.PixelsToUnits(gui.GetFontSize().lineSize);
            contentSize = scrollArea.MeasureContent(contentSize.X, gui);
            gui.DrawPanel(new Rect(gui.statePosition.X, gui.statePosition.Y, firstColumnWidth, contentSize.Y), firstColumContent);
        }

        // Add padding to the left, so the scrollArea is moved to the right, next to the firstColumn panel
        using (gui.EnterGroup(new Padding(firstColumnWidth, 0, 0, 0), RectAllocator.LeftAlign, spacing: 0f)) {
            scrollArea.Build(gui, contentSize.Y);
        }
    }

    private async Task AutoBalance() {
        try {
            // The things we've already updated, and the error each had before their previous update.
            Queue<ProductionLink> updateOrder = [];
            Dictionary<ProductionLink, float> previousUpdates = [];
            int negativeFeedbackCount = 0;

            for (int i = 0; i < 1000; i++) { // No matter what else happens, give up after 1000 clicks.
                float? oldExcess = null;
                GoodDetails details;
                float excess;
                ProductionLink? link;

                while (true) {
                    // Look for a product that (a) has a mismatch, (b) has exactly one link in the displayed pages, and (c) hasn't been updated yet.
                    (details, excess, link) = GoodsToBalance()
                        // Find the link
                        .Select(x => (x.details, x.excess, link: DisplayTables.Select(t => t.links.FirstOrDefault(l => l.goods.QualityName() == x.name && l.amount != 0)).WhereNotNull().SingleOrDefault(false)))
                        // Find an item with exactly one link that hasn't been updated yet. (Or has been removed to allow it to be updated again.)
                        .FirstOrDefault(x => x.link != null && !previousUpdates.ContainsKey(x.link));

                    if (link != null) { break; }

                    if (updateOrder.Count == 0) {
                        return;
                    }
                    // If nothing was found, allow the least-recently updated product to be updated again, but remember its previous state so we can tell if we're making progress.
                    ProductionLink removedLink = updateOrder.Dequeue();
                    oldExcess = previousUpdates[removedLink];
                    _ = previousUpdates.Remove(removedLink);
                }

                if (oldExcess - Math.Abs(excess) <= Epsilon) {
                    // TODO: The negative feedback detection sometimes gets triggered, but sometimes the `updateOrder.Count == 0` check gets triggered instead.
                    // This probably needs to be revisited when fixing https://github.com/shpaass/yafc-ce/issues/169
                    // The previous positive difference for this product is less than the current (supposedly better) positive difference.
                    if (++negativeFeedbackCount > 3) {
                        return; // Too many negative feedbacks detected; give up.
                    }
                    else { // Skip this for now, in hopes of getting a better result later.
                        updateOrder.Enqueue(link);
                        previousUpdates[link] = Math.Abs(excess);
                        continue;
                    }
                }

                updateOrder.Enqueue(link);
                previousUpdates[link] = Math.Abs(excess);

                await SummaryDataColumn.SetProviderAmount(link, (ProjectPage)link.owner.owner, details.sum);
            }
            _ = model.showOnlyIssues;
        }
        finally {
            // HACK: Nothing left to change, but force a recalculate anyway, so the user doesn't have to view all the tabs before clicking Auto balance again.
            // Sometimes this will cause updates that require an additional button click.
            // This is probably related to https://github.com/shpaass/yafc-ce/issues/169
            foreach (ProjectPage page in DisplayPages) {
                await page.RunSolveJob();
            }
        }
    }

    private IEnumerable<ProductionTable> DisplayTables => project.displayPages.Select(p => project.FindPage(p)?.content as ProductionTable).WhereNotNull();
    private IEnumerable<ProjectPage> DisplayPages => DisplayTables.Select(t => (ProjectPage)t.owner);

    private void BuildScrollArea(ImGui gui) {
        foreach (ProjectPage page in DisplayPages) {
            _ = mainGrid.BuildRow(gui, page);
        }
    }

    private void Recalculate() => Recalculate(false);

    private void Recalculate(bool visualOnly) {
        Dictionary<string, GoodDetails> newGoods = [];

        foreach (Guid displayPage in project.displayPages) {
            ProjectPage? page = project.FindPage(displayPage);
            ProductionTable? content = page?.content as ProductionTable;

            if (content == null) {
                continue;
            }

            foreach (IProductionLink link in content.allLinks) {
                if (link.amount != 0f) {
                    GoodDetails value = newGoods.GetValueOrDefault(link.goods.QualityName());
                    value.totalProvided += YafcRounding(link.amount); ;
                    newGoods[link.goods.QualityName()] = value;
                }
            }

            foreach (ProductionTableFlow flow in content.flow) {
                if (flow.amount < -Epsilon) {
                    GoodDetails value = newGoods.GetValueOrDefault(flow.goods.QualityName());
                    value.totalNeeded -= YafcRounding(flow.amount); ;
                    value.sum -= YafcRounding(flow.amount); ;
                    newGoods[flow.goods.QualityName()] = value;
                }
                else if (flow.amount > Epsilon) {
                    if (!content.allLinks.Any(x => x.goods == flow.goods)) {
                        // Only count extras if not linked
                        GoodDetails value = newGoods.GetValueOrDefault(flow.goods.QualityName());
                        value.extraProduced += YafcRounding(flow.amount);
                        value.sum -= YafcRounding(flow.amount);
                        newGoods[flow.goods.QualityName()] = value;
                    }
                }
            }
        }

        int count = 0;
        foreach (KeyValuePair<string, GoodDetails> entry in newGoods) {
            float amountAvailable = YafcRounding((entry.Value.totalProvided > 0 ? entry.Value.totalProvided : 0) + entry.Value.extraProduced);
            float amountNeeded = YafcRounding((entry.Value.totalProvided < 0 ? -entry.Value.totalProvided : 0) + entry.Value.totalNeeded);

            if (model != null && model.showOnlyIssues && (Math.Abs(amountAvailable - amountNeeded) < Epsilon || amountNeeded == 0)) {
                continue;
            }
            count++;
        }

        goodsColumn.width = count * (ElementWidth + ElementSpacing);

        allGoods = newGoods;

        Rebuild(visualOnly);
        scrollArea.RebuildContents();
    }

    // Convert/truncate value as shown in UI to prevent slight mismatches
    private static float YafcRounding(float value) {
        _ = DataUtils.TryParseAmount(DataUtils.FormatAmount(value, UnitOfMeasure.PerSecond), out float result, UnitOfMeasure.PerSecond);
        return result;
    }

    public override void SetSearchQuery(SearchQuery query) {
        searchQuery = query;
        bodyContent.Rebuild();
        scrollArea.Rebuild();
    }
}
