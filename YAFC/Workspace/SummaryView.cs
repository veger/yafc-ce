using System;
using System.Collections.Generic;
using YAFC.Model;
using YAFC.UI;

namespace YAFC
{
    public class SummaryView : ProjectPageView<Summary>
    {
        static readonly float Epsilon = 1e-5f;
        static readonly float ElementWidth = 3;
        static readonly float ElementSpacing = 1;
        struct GoodDetails
        {
            public float totalProvided;
            public float totalNeeded;
            public float extraProduced;
            public float sum;
        }

        private Project project;

        private readonly ScrollArea scrollArea;
        private readonly DataColumn<ProjectPage> goodsColumn;
        private readonly DataGrid<ProjectPage> mainGrid;

        private readonly Dictionary<string, GoodDetails> allGoods = new Dictionary<string, GoodDetails>();

        public SummaryView()
        {
            goodsColumn = new DataColumn<ProjectPage>("Linked", BuildSummaryTable, null, 30f);
            var columns = new[]
            {
                new DataColumn<ProjectPage>("Tab", BuildTabName, null, 6f),
                goodsColumn,
            };
            // TODO Make height relative to min(window,content) height instead of fixed
            scrollArea = new ScrollArea(30, BuildScrollArea, vertical: true, horizontal: true);
            mainGrid = new DataGrid<ProjectPage>(columns);
        }

        public void SetProject(Project project)
        {
            if (this.project != null)
            {
                this.project.metaInfoChanged -= Recalculate;
                foreach (var page in project.pages)
                {
                    page.contentChanged -= Recalculate;
                }
            }

            this.project = project;

            project.metaInfoChanged += Recalculate;
            foreach (var page in project.pages)
            {
                page.contentChanged += Recalculate;
            }
        }

        protected override void BuildPageTooltip(ImGui gui, Summary contents)
        {
        }

        protected void BuildTabName(ImGui gui, ProjectPage page)
        {
            if (page?.contentType != typeof(ProductionTable))
            {
                return;
            }
            using (gui.EnterGroup(new Padding(0.5f, 0.2f, 0.2f, 0.5f)))
            {
                gui.spacing = 0.2f;
                if (page.icon != null)
                    gui.BuildIcon(page.icon.icon);
                else gui.AllocateRect(0f, 1.5f);
                gui.BuildText(page.name);
            }
        }

        protected void BuildSummaryTable(ImGui gui, ProjectPage page)
        {
            if (page?.contentType != typeof(ProductionTable))
            {
                return;
            }

            var table = page.content as ProductionTable;

            using (var grid = gui.EnterInlineGrid(ElementWidth, ElementSpacing))
            {
                foreach (KeyValuePair<string, GoodDetails> goodInfo in allGoods)
                {
                    float amountAvailable = YAFCRounding((goodInfo.Value.totalProvided > 0 ? goodInfo.Value.totalProvided : 0) + goodInfo.Value.extraProduced);
                    var amountNeeded = YAFCRounding((goodInfo.Value.totalProvided < 0 ? -goodInfo.Value.totalProvided : 0) + goodInfo.Value.totalNeeded);
                    if (model.showOnlyIssues && (Math.Abs(amountAvailable - amountNeeded) < Epsilon || amountNeeded == 0))
                    {
                        continue;
                    }

                    grid.Next();
                    bool enoughProduced = amountAvailable >= amountNeeded;
                    ProductionLink link = table.links.Find(x => x.goods.name == goodInfo.Key);
                    if (link != null)
                    {
                        if (link.amount != 0f)
                        {
                            DrawProvideProduct(gui, link, page, goodInfo.Value, enoughProduced);
                        }
                    }
                    else
                    {
                        if (Array.Exists(table.flow, x => x.goods.name == goodInfo.Key))
                        {
                            var flow = Array.Find(table.flow, x => x.goods.name == goodInfo.Key);
                            if (Math.Abs(flow.amount) > Epsilon)
                            {

                                DrawRequestProduct(gui, flow, enoughProduced);
                            }
                        }
                    }
                }
            }
        }

        private void DrawProvideProduct(ImGui gui, ProductionLink element, ProjectPage page, GoodDetails goodInfo, bool enoughProduced)
        {
            gui.allocator = RectAllocator.Stretch;
            gui.spacing = 0f;

            var evt = gui.BuildFactorioObjectWithEditableAmount(element.goods, element.amount, element.goods.flowUnitOfMeasure, out float newAmount, (element.amount > 0 && enoughProduced) || (element.amount < 0 && goodInfo.extraProduced == -element.amount) ? SchemeColor.Primary : SchemeColor.Error);
            if (evt == GoodsWithAmountEvent.TextEditing && newAmount != 0)
            {
                SetProviderAmount(element, page, newAmount);
            }
            else if (evt == GoodsWithAmountEvent.ButtonClick)
            {
                SetProviderAmount(element, page, YAFCRounding(goodInfo.sum));
            }
        }

        private void DrawRequestProduct(ImGui gui, ProductionTableFlow flow, bool enoughProduced)
        {
            gui.allocator = RectAllocator.Stretch;
            gui.spacing = 0f;
            gui.BuildFactorioObjectWithAmount(flow.goods, -flow.amount, flow.goods?.flowUnitOfMeasure ?? UnitOfMeasure.None, flow.amount > Epsilon ? enoughProduced ? SchemeColor.Green : SchemeColor.Error : SchemeColor.None);
        }

        protected override void BuildHeader(ImGui gui)
        {
            base.BuildHeader(gui);

            gui.allocator = RectAllocator.Center;
            gui.BuildText("Production Sheet Summary", Font.header, false, RectAlignment.Middle);
            gui.allocator = RectAllocator.LeftAlign;
        }

        protected override void BuildContent(ImGui gui)
        {
            if (gui.BuildCheckBox("Only show issues", model.showOnlyIssues, out var newValue))
            {
                model.showOnlyIssues = newValue;
                Recalculate();
            }

            scrollArea.Build(gui);
        }

        private void BuildScrollArea(ImGui gui)
        {
            foreach (var displayPage in project.displayPages)
            {
                var page = project.FindPage(displayPage);
                if (page?.contentType != typeof(ProductionTable))
                    continue;

                mainGrid.BuildRow(gui, page);
            }
        }

        // Convert/truncate value as shown in UI to prevent slight mismatches
        private float YAFCRounding(float value)
        {
            DataUtils.TryParseAmount(DataUtils.FormatAmount(value, UnitOfMeasure.Second), out float result, UnitOfMeasure.Second);
            return result;
        }

        private void Recalculate() => Recalculate(false);

        private void Recalculate(bool visualOnly)
        {
            allGoods.Clear();
            foreach (var displayPage in project.displayPages)
            {
                var page = project.FindPage(displayPage);
                var content = page?.content as ProductionTable;
                if (content == null)
                {
                    continue;
                }

                foreach (var link in content.links)
                {
                    if (link.amount != 0f)
                    {
                        var value = allGoods.GetValueOrDefault(link.goods.name);
                        value.totalProvided += YAFCRounding(link.amount); ;
                        allGoods[link.goods.name] = value;
                    }
                }

                foreach (var flow in content.flow)
                {
                    if (flow.amount < -Epsilon)
                    {
                        var value = allGoods.GetValueOrDefault(flow.goods.name);
                        value.totalNeeded -= YAFCRounding(flow.amount); ;
                        value.sum -= YAFCRounding(flow.amount); ;
                        allGoods[flow.goods.name] = value;
                    }
                    else if (flow.amount > Epsilon)
                    {
                        if (!content.links.Exists(x => x.goods == flow.goods))
                        {
                            // Only count extras if not linked
                            var value = allGoods.GetValueOrDefault(flow.goods.name);
                            value.extraProduced += YAFCRounding(flow.amount);
                            value.sum -= YAFCRounding(flow.amount);
                            allGoods[flow.goods.name] = value;
                        }
                    }
                }
            }

            var count = 0;
            foreach (var entry in allGoods)
            {
                var amountAvailable = YAFCRounding((entry.Value.totalProvided > 0 ? entry.Value.totalProvided : 0) + entry.Value.extraProduced);
                var amountNeeded = YAFCRounding((entry.Value.totalProvided < 0 ? -entry.Value.totalProvided : 0) + entry.Value.totalNeeded);
                if (model != null && model.showOnlyIssues && (Math.Abs(amountAvailable - amountNeeded) < Epsilon || amountNeeded == 0))
                {
                    continue;
                }
                count++;
            }

            goodsColumn.width = count * (ElementWidth + ElementSpacing);

            Rebuild(visualOnly);
            scrollArea.RebuildContents();
        }

        private void SetProviderAmount(ProductionLink element, ProjectPage page, float newAmount)
        {
            element.RecordUndo().amount = newAmount;
            // Hack force recalculate the page 9and make sure to catch the content change event caused by the recalculation)
            page.SetActive(true);
            page.SetToRecalculate();
            page.SetActive(false);
        }

        public override void CreateModelDropdown(ImGui gui, Type type, Project project)
        {
        }
    }
}