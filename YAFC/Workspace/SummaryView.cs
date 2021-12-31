using System;
using System.Collections.Generic;
using YAFC.Model;
using YAFC.UI;

namespace YAFC
{
    public class SummaryView : ProjectPageView<Summary>
    {
        static readonly float Epsilon = 1e-5f;
        struct GoodDetails
        {
            public float totalProvided;
            public float totalNeeded;
            public float extraProduced;
        }

        private readonly MainScreen screen;

        private readonly DataGrid<ProjectPage> mainGrid;

        private readonly Dictionary<string, GoodDetails> allGoods = new Dictionary<string, GoodDetails>();

        private ProjectPage invokedPage;

        public SummaryView(MainScreen screen)
        {
            this.screen = screen;
            var columns = new[]
            {
                new DataColumn<ProjectPage>("Tab", BuildTabName, null, 6f),
                new DataColumn<ProjectPage>("Linked", BuildLinkedItems, null, float.MaxValue),
            };
            mainGrid = new DataGrid<ProjectPage>(columns);
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

        protected void BuildLinkedItems(ImGui gui, ProjectPage page)
        {
            if (page?.contentType != typeof(ProductionTable))
            {
                return;
            }

            var table = page.content as ProductionTable;

            using (var grid = gui.EnterInlineGrid(3f, 1f))
            {
                foreach (KeyValuePair<string, GoodDetails> entry in allGoods)
                {
                    var amountAvailable = YAFCRounding((entry.Value.totalProvided > 0 ? entry.Value.totalProvided : 0) + entry.Value.extraProduced);
                    var amountNeeded = YAFCRounding((entry.Value.totalProvided < 0 ? -entry.Value.totalProvided : 0) + entry.Value.totalNeeded);
                    if (Math.Abs(amountAvailable - amountNeeded) < Epsilon || amountNeeded == 0)
                    {
                        continue;
                    }

                    grid.Next();
                    var link = table.links.Find(x => x.goods.name == entry.Key);
                    if (link != null)
                    {
                        if (link.amount != 0f)
                        {
                            DrawProvideProduct(gui, link, page, entry.Value.extraProduced, amountAvailable >= amountNeeded);
                        }
                    }
                    else
                    {
                        if (Array.Exists(table.flow, x => x.goods.name == entry.Key))
                        {
                            var flow = Array.Find(table.flow, x => x.goods.name == entry.Key);
                            if (Math.Abs(flow.amount) > Epsilon)
                            {

                                DrawRequestProduct(gui, flow, amountAvailable >= amountNeeded);
                            }
                        }
                    }
                }
            }
        }

        private void DrawProvideProduct(ImGui gui, ProductionLink element, ProjectPage page, float extraProduced, bool enoughOutput)
        {
            gui.allocator = RectAllocator.Stretch;
            gui.spacing = 0f;

            var evt = gui.BuildFactorioObjectWithEditableAmount(element.goods, element.amount, element.goods.flowUnitOfMeasure, out var newAmount, (element.amount > 0 && enoughOutput) || (element.amount < 0 && extraProduced == -element.amount) ? SchemeColor.Primary : SchemeColor.Error);
            if (evt == GoodsWithAmountEvent.TextEditing && newAmount != 0)
            {
                element.RecordUndo().amount = newAmount;
                // Hack force recalculate the page 9and make sure to catch the content change event caused by the recalculation)
                invokedPage = page;
                page.contentChanged += RebuildInvoked;
                page.SetActive(true);
                page.SetToRecalculate();
                page.SetActive(false);
            }
        }

        private void DrawRequestProduct(ImGui gui, ProductionTableFlow flow, bool enoughProduced)
        {
            gui.allocator = RectAllocator.Stretch;
            gui.spacing = 0f;
            gui.BuildFactorioObjectWithAmount(flow.goods, -flow.amount, flow.goods?.flowUnitOfMeasure ?? UnitOfMeasure.None, flow.amount > Epsilon ? enoughProduced ? SchemeColor.Green : SchemeColor.Error : SchemeColor.None);
        }

        protected override void BuildContent(ImGui gui)
        {
            // TODO Can we detect if things changed?
            allGoods.Clear();
            foreach (var displayPage in screen.project.displayPages)
            {
                var page = screen.project.FindPage(displayPage);
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
                        allGoods[flow.goods.name] = value;
                    }
                    else if (flow.amount > Epsilon)
                    {
                        if (!content.links.Exists(x => x.goods == flow.goods))
                        {
                            // Only count extras if not linked
                            var value = allGoods.GetValueOrDefault(flow.goods.name);
                            value.extraProduced += YAFCRounding(flow.amount);
                            allGoods[flow.goods.name] = value;
                        }
                    }
                }
            }

            foreach (var displayPage in screen.project.displayPages)
            {
                var page = screen.project.FindPage(displayPage);
                mainGrid.BuildRow(gui, page);
            }
        }

        // Convert/truncate value as shown in UI to prevent slight mismatches
        private float YAFCRounding(float value)
        {
            DataUtils.TryParseAmount(DataUtils.FormatAmount(value, UnitOfMeasure.Second), out float result, UnitOfMeasure.Second);
            return result;
        }

        private void RebuildInvoked(bool visualOnly = false)
        {
            Rebuild(visualOnly);
            invokedPage.contentChanged -= RebuildInvoked;
        }

        public override void CreateModelDropdown(ImGui gui, Type type, Project project)
        {
        }
    }
}