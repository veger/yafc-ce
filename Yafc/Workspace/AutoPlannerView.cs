﻿using System;
using System.Collections.Generic;
using Yafc.I18n;
using Yafc.Model;
using Yafc.UI;

#nullable disable warnings // Disabling nullable for legacy code.

namespace Yafc;

public class AutoPlannerView : ProjectPageView<AutoPlanner> {
    private AutoPlannerRecipe selectedRecipe;

    public override void SetModel(ProjectPage? page) {
        base.SetModel(page);
        selectedRecipe = null;
    }

    protected override void BuildPageTooltip(ImGui gui, AutoPlanner contents) {
        using var grid = gui.EnterInlineGrid(3f, 1f);
        foreach (var goal in contents.goals) {
            grid.Next();
            _ = gui.BuildFactorioObjectWithAmount(goal.item, new(goal.amount, goal.item.flowUnitOfMeasure), ButtonDisplayStyle.ProductionTableUnscaled);
        }
    }

    private Action CreateAutoPlannerWizard(List<WizardPanel.PageBuilder> pages) {
        List<AutoPlannerGoal> goal = [];
        string pageName = LSs.AutoPlanner;

        void page1(ImGui gui, ref bool valid) {
            gui.BuildText(LSs.AutoPlannerWarning,
                TextBlockDisplayStyle.ErrorText);
            gui.BuildText(LSs.AutoPlannerPageName);
            _ = gui.BuildTextInput(pageName, out pageName, null);
            gui.AllocateSpacing(2f);
            gui.BuildText(LSs.AutoPlannerGoal);
            using (var grid = gui.EnterInlineGrid(3f)) {
                for (int i = 0; i < goal.Count; i++) {
                    var elem = goal[i];
                    grid.Next();
                    DisplayAmount amount = new(elem.amount, elem.item.flowUnitOfMeasure);
                    if (gui.BuildFactorioObjectWithEditableAmount(elem.item, amount, ButtonDisplayStyle.ProductionTableUnscaled) == GoodsWithAmountEvent.TextEditing) {
                        if (amount.Value != 0f) {
                            elem.amount = amount.Value;
                        }
                        else {
                            goal.RemoveAt(i--);
                        }
                    }
                }
                grid.Next();
                if (gui.BuildButton(Icon.Plus, SchemeColor.Primary, SchemeColor.PrimaryAlt, size: 2.5f)) {
                    SelectSingleObjectPanel.Select(Database.goods.explorable, new(LSs.AutoPlannerSelectProductionGoal), x => {
                        goal.Add(new AutoPlannerGoal { amount = 1f, item = x });
                        gui.Rebuild();
                    });
                }
                grid.Next();
            }
            gui.AllocateSpacing(2f);
            gui.BuildText(LSs.AutoPlannerReviewMilestones, TextBlockDisplayStyle.WrappedText);
            new MilestonesWidget().Build(gui);
            gui.AllocateSpacing(2f);
            valid = !string.IsNullOrEmpty(pageName) && goal.Count > 0;
        }

        pages.Add(page1);
        return () => {
            var planner = MainScreen.Instance.AddProjectPage(LSs.AutoPlanner, goal[0].item, typeof(AutoPlanner), false, false);
            (planner.content as AutoPlanner).goals.AddRange(goal);
            MainScreen.Instance.SetActivePage(planner);
        };
    }

    protected override void BuildContent(ImGui gui) {
        if (model == null) {
            return;
        }

        if (model.tiers == null) {
            return;
        }

        foreach (var tier in model.tiers) {
            using var grid = gui.EnterInlineGrid(3f);

            foreach (var recipe in tier) {
                var color = SchemeColor.None;

                if (gui.isBuilding) {
                    if (selectedRecipe != null && ((selectedRecipe.downstream != null && selectedRecipe.downstream.Contains(recipe.recipe)) ||
                                                   (selectedRecipe.upstream != null && selectedRecipe.upstream.Contains(recipe.recipe)))) {
                        color = SchemeColor.Secondary;
                    }
                }

                grid.Next();

                if (gui.BuildFactorioObjectWithAmount(recipe.recipe, new(recipe.recipesPerSecond, UnitOfMeasure.PerSecond), ButtonDisplayStyle.ProductionTableScaled(color)) == Click.Left) {
                    selectedRecipe = recipe;
                }
            }
        }
    }

    /*
    public override void CreateModelDropdown(ImGui gui, Type type, Project project) {
        if (gui.BuildContextMenuButton("Auto planner (Alpha)"))
        {
            close = true;
            WizardPanel.Show("New auto planner", CreateAutoPlannerWizard);
        }
    }
    */
}
