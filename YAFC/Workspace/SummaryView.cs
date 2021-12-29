using System;
using YAFC.Model;
using YAFC.UI;

namespace YAFC
{
    public class SummaryView : ProjectPageView<Summary>
    {
        private readonly MainScreen screen;

        private readonly DataGrid<ProjectPage> mainGrid;

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

            using (var grid = gui.EnterInlineGrid(3f, 1f))
            {
                foreach (var link in (page.content as ProductionTable).links)
                {
                    if (link.amount != 0f)
                    {
                        grid.Next();
                        DrawProvideProduct(gui, link, page);
                    }

                }

                foreach (var flow in (page.content as ProductionTable).flow)
                {
                    if (flow.amount >= -1e-5f)
                        break;
                    grid.Next();
                    DrawRequestProduct(gui, flow);
                }
            }
        }

        private void DrawProvideProduct(ImGui gui, ProductionLink element, ProjectPage page)
        {
            gui.allocator = RectAllocator.Stretch;
            gui.spacing = 0f;
            var evt = gui.BuildFactorioObjectWithEditableAmount(element.goods, element.amount, element.goods.flowUnitOfMeasure, out var newAmount, SchemeColor.Primary);
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

        private void DrawRequestProduct(ImGui gui, ProductionTableFlow flow)
        {
            gui.allocator = RectAllocator.Stretch;
            gui.spacing = 0f;
            gui.BuildFactorioObjectWithAmount(flow.goods, -flow.amount, flow.goods?.flowUnitOfMeasure ?? UnitOfMeasure.None, SchemeColor.None);
        }

        protected override void BuildContent(ImGui gui)
        {

            foreach (var displayPage in screen.project.displayPages)
            {
                var page = screen.project.FindPage(displayPage);
                mainGrid.BuildRow(gui, page);
            }
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