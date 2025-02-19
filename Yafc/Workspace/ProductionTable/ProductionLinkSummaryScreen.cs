using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using SDL2;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

public class ProductionLinkSummaryScreen : PseudoScreen, IComparer<(RecipeRow row, float flow)> {
    private readonly Stack<ProductionLink> links = new();

    private ProductionLink link;
    private List<(RecipeRow row, float flow)> input;
    private List<(RecipeRow row, float flow)> output;
    private float totalInput, totalOutput;
    private readonly ScrollArea scrollArea;

    private ProductionLinkSummaryScreen(ProductionLink link) {
        scrollArea = new ScrollArea(30, BuildScrollArea);
        CalculateFlow(link);
    }

    private void BuildScrollArea(ImGui gui) {
        gui.BuildText("Production: " + DataUtils.FormatAmount(totalInput, link.flowUnitOfMeasure), Font.subheader);
        BuildFlow(gui, input, totalInput, false);
        gui.spacing = 0.5f;
        gui.BuildText("Consumption: " + DataUtils.FormatAmount(totalOutput, link.flowUnitOfMeasure), Font.subheader);
        BuildFlow(gui, output, totalOutput, true);
        if (link.amount != 0) {
            gui.spacing = 0.5f;
            gui.BuildText((link.amount > 0 ? "Requested production: " : "Requested consumption: ") + DataUtils.FormatAmount(MathF.Abs(link.amount),
                link.flowUnitOfMeasure), new TextBlockDisplayStyle(Font.subheader, Color: SchemeColor.GreenAlt));
        }
        if (link.flags.HasFlags(ProductionLink.Flags.LinkNotMatched) && totalInput != totalOutput + link.amount) {
            float amount = totalInput - totalOutput - link.amount;
            gui.spacing = 0.5f;
            gui.BuildText((amount > 0 ? "Overproduction: " : "Overconsumption: ") + DataUtils.FormatAmount(MathF.Abs(amount), link.flowUnitOfMeasure),
                new TextBlockDisplayStyle(Font.subheader, Color: SchemeColor.Error));
        }
        ShowRelatedLinks(gui);
    }

    private void ShowRelatedLinks(ImGui gui) {
        ModelObject o = link;
        List<ModelObject> parents = [];
        while (o.ownerObject is not ProjectPage) {
            var t = o.ownerObject;
            if (t is null) {
                break;
            }
            o = t;
            if (t == o.ownerObject) continue;
            parents.Add(t);
        }
        if (o is ProductionTable page) {
            Dictionary<ProductionLink, List<(RecipeRow row, float flow)>> childLinks = [], parentLinks = [], otherLinks = [];
            List<(RecipeRow row, float flow)> unlinked = [], table;
            foreach (var (row, relationLinks) in
                page.GetAllRecipes(link.owner)
                .Select(e => (e, IsLinkParent(e, parents) ? parentLinks : otherLinks))
                .Concat(link.owner.GetAllRecipes().Select(e => (e, childLinks)))) {
                if (isPartOfCurrentLink(row)
                    || isNotRelatedToCurrentLink(row)) {
                    continue;
                }
                float localFlow = DetermineFlow(link.goods, row);

                if ((row.FindLink(link.goods, out var otherLink) && otherLink != link)) {
                    if (!relationLinks.ContainsKey(otherLink)) {
                        relationLinks.Add(otherLink, []);
                    }
                    table = relationLinks[otherLink];
                }
                else {
                    table = unlinked;
                }

                table.Add((row, localFlow));
            }
            var color = 1;
            gui.spacing = 0.75f;
            if (childLinks.Values.Any(e => e.Any())) {
                gui.BuildText("Child links: ", Font.productionTableHeader);
                foreach (var relTable in childLinks.Values) {
                    BuildFlow(gui, relTable, relTable.Sum(e => Math.Abs(e.flow)), false, color++);
                }
            }
            if (parentLinks.Values.Any(e => e.Any())) {
                gui.BuildText("Parent links: ", Font.productionTableHeader);
                foreach (var relTable in parentLinks.Values) {
                    BuildFlow(gui, relTable, relTable.Sum(e => Math.Abs(e.flow)), false, color++);
                }
            }
            if (otherLinks.Values.Any(e => e.Any())) {
                gui.BuildText("Unrelated links: ", Font.productionTableHeader);
                foreach (var relTable in otherLinks.Values) {
                    BuildFlow(gui, relTable, relTable.Sum(e => Math.Abs(e.flow)), false, color++);
                }
            }
            if (unlinked.Any()) {
                gui.BuildText("Unlinked: ", Font.subheader);
                BuildFlow(gui, unlinked, 0, false);
            }
        }
    }

    private bool isNotRelatedToCurrentLink(RecipeRow? row) => (!row.Ingredients.Any(e => e.Goods == link.goods)
                        && !row.Products.Any(e => e.Goods == link.goods)
                        && !(row.fuel is not null && row.fuel == link.goods));
    private bool isPartOfCurrentLink(RecipeRow row) => link.capturedRecipes.Any(e => e == row);
    private bool IsLinkParent(RecipeRow row, List<ModelObject> parents) => row.Ingredients.Select(e => e.Link).Concat(row.Products.Select(e => e.Link)).Append(row.FuelInformation.Link)
        .Where(e => e?.ownerObject is not null)
        .Where(e => e!.goods == link.goods)
        .Any(e => parents.Contains(e!.ownerObject!));

    public override void Build(ImGui gui) {
        BuildHeader(gui, "Link summary");
        using (gui.EnterRow()) {
            gui.BuildText("Exploring link for: ", topOffset: 0.5f);
            _ = gui.BuildFactorioObjectButtonWithText(link.goods, tooltipOptions: DrawParentRecipes(link.owner, "link"));
        }
        scrollArea.Build(gui);
        if (gui.BuildButton("Remove link", link.owner.links.Contains(link) ? SchemeColor.Primary : SchemeColor.Grey)) {
            DestroyLink(link);
            Close();
        }
        if (gui.BuildButton("Done")) {
            Close();
        }
    }

    private void DestroyLink(ProductionLink link) {
        _ = link.owner.RecordUndo().links.Remove(link);
        Rebuild();
    }

    protected override void ReturnPressed() => Close();

    private void BuildFlow(ImGui gui, List<(RecipeRow row, float flow)> list, float total, bool isLinkOutput, int colorIndex = 0) {
        gui.spacing = 0f;
        foreach (var (row, flow) in list) {
            string amount = DataUtils.FormatAmount(flow, link.flowUnitOfMeasure);
            if (gui.BuildFactorioObjectButtonWithText(row.recipe, amount, tooltipOptions: DrawParentRecipes(row.owner, "recipe")) == Click.Left) {
                // Find the corresponding links associated with the clicked recipe,
                IEnumerable<IObjectWithQuality<Goods>> goods = (isLinkOutput ? row.Products.Select(p => p.Goods) : row.Ingredients.Select(i => i.Goods))!;
                Dictionary<IObjectWithQuality<Goods>, ProductionLink> links = goods
                    .Select(goods => { row.FindLink(goods, out ProductionLink? link); return (goods, link: link!); })
                    .Where(x => x.link != null)
                    .ToDictionary(x => x.goods, x => x.link);
                links.Remove(link.goods); // except the current one, only applicable for recipes like kovarex that have catalysts.

                // Jump to the only link, or offer a selection of links to inspect next.
                if (row.FindLink(link.goods, out var otherLink) && otherLink != link) {
                    changeLinkView(otherLink);
                }
                else if (links.Count == 1) {
                    changeLinkView(links.First().Value);
                }
                else {
                    gui.ShowDropDown(drawLinks);
                }

                void drawLinks(ImGui gui) {
                    if (links.Count == 0) {
                        string text = isLinkOutput ? "This recipe has no linked products." : "This recipe has no linked ingredients";
                        gui.BuildText(text, TextBlockDisplayStyle.ErrorText);
                    }
                    else {
                        IComparer<IObjectWithQuality<Goods>> comparer = DataUtils.DefaultOrdering;
                        if (isLinkOutput && row.recipe.mainProduct().Is<Goods>(out var mainProduct)) {
                            comparer = DataUtils.CustomFirstItemComparer(mainProduct, comparer);
                        }

                        string header = isLinkOutput ? "Select product link to inspect" : "Select ingredient link to inspect";
                        ObjectSelectOptions<IObjectWithQuality<Goods>> options = new(header, comparer, int.MaxValue);
                        if (gui.BuildInlineObjectList(links.Keys, out IObjectWithQuality<Goods>? selected, options) && gui.CloseDropdown()) {
                            changeLinkView(links[selected]);
                        }
                    }
                }
            }

            if (gui.isBuilding) {
                var lastRect = gui.lastRect;
                lastRect.Width *= Math.Abs(flow / total);
                gui.DrawRectangle(lastRect, GetFlowColor(colorIndex));
            }
        }

        void changeLinkView(ProductionLink newLink) {
            links.Push(link);
            CalculateFlow(newLink);
        }
    }

    private static SchemeColor GetFlowColor(int colorIndex) => (colorIndex % 7) switch {
        0 => SchemeColor.Primary,
        1 => SchemeColor.Secondary,
        2 => SchemeColor.Green,
        3 => SchemeColor.Magenta,
        4 => SchemeColor.Grey,
        5 => SchemeColor.TagColorRed,
        6 => SchemeColor.TagColorYellow,
        _ => throw new UnreachableException(),
    };

    /// <summary>
    /// Returns a delegate that will be called when drawing <see cref="ObjectTooltip"/>, to provide nesting information when hovering recipes and links.
    /// </summary>
    /// <param name="table">The first table to consider when determining what recipe rows to report.</param>
    /// <param name="type">"link" or "recipe", for the text "This X is nested under:"</param>
    /// <returns>A <see cref="DrawBelowHeader"/> that will draw the appropriate information when called.</returns>
    private static DrawBelowHeader DrawParentRecipes(ProductionTable table, string type) => gui => {
        // Collect the parent recipes (equivalently, the table headers of all tables that contain table)
        Stack<RecipeRow> parents = new();
        while (table?.owner is RecipeRow row) {
            parents.Push(row);
            table = row.owner;
        }

        if (parents.Count > 0) {
            gui.BuildText($"This {type} is nested under:", TextBlockDisplayStyle.WrappedText);

            // Draw the parents with nesting
            float padding = 0.5f;
            while (parents.Count > 0) {
                IconDisplayStyle style = IconDisplayStyle.Default with { MilestoneDisplay = MilestoneDisplay.None };
                gui.BuildFactorioObjectButtonWithText(parents.Pop().recipe, iconDisplayStyle: style, indent: padding);
                padding += 0.5f;
            }
        }
    };

    public override bool KeyDown(SDL.SDL_Keysym key) {
        if (key.scancode is SDL.SDL_Scancode.SDL_SCANCODE_BACKSPACE or SDL.SDL_Scancode.SDL_SCANCODE_KP_BACKSPACE && links.Count > 0) {
            CalculateFlow(links.Pop());
            return true;
        }
        return base.KeyDown(key);
    }

    [MemberNotNull(nameof(link), nameof(input), nameof(output))]
    private void CalculateFlow(ProductionLink link) {
        // changeLinkView calls this while iterating over one of these lists. We must create new lists rather than editing the existing ones.
        List<(RecipeRow row, float flow)> input = [], output = [];
        totalInput = totalOutput = 0;
        foreach (var recipe in link.capturedRecipes) {
            float localFlow = DetermineFlow(link.goods, recipe);
            if (localFlow > 0) {
                input.Add((recipe, localFlow));
                totalInput += localFlow;
            }
            else if (localFlow < 0) {
                output.Add((recipe, -localFlow));
                totalOutput -= localFlow;
            }
        }
        input.Sort(this);
        output.Sort(this);

        this.input = input;
        this.output = output;
        this.link = link;

        Rebuild();
        scrollArea.RebuildContents();
    }

    private static float DetermineFlow(IObjectWithQuality<Goods> goods, RecipeRow recipe) {
        float production = recipe.GetProductionForRow(goods);
        float consumption = recipe.GetConsumptionForRow(goods);
        float fuelUsage = recipe.fuel == goods ? recipe.FuelInformation.Amount : 0;
        float localFlow = production - consumption - fuelUsage;
        return localFlow;
    }

    public static void Show(ProductionLink link) => _ = MainScreen.Instance.ShowPseudoScreen(new ProductionLinkSummaryScreen(link));

    public int Compare((RecipeRow row, float flow) x, (RecipeRow row, float flow) y) => y.flow.CompareTo(x.flow);
}
