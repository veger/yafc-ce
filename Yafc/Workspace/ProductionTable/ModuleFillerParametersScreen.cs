using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

public class ModuleFillerParametersScreen : PseudoScreen {
    private static readonly float ModulesMinPayback = MathF.Log(600f);
    private static readonly float ModulesMaxPayback = MathF.Log(3600f * 120f);
    private readonly ModuleFillerParameters modules;
    private readonly VirtualScrollList<KeyValuePair<EntityCrafter, BeaconOverrideConfiguration>> overrideList;

    public static void Show(ModuleFillerParameters parameters) => _ = MainScreen.Instance.ShowPseudoScreen(new ModuleFillerParametersScreen(parameters));

    private ModuleFillerParametersScreen(ModuleFillerParameters modules) {
        this.modules = modules;
        overrideList = new(11, new(3.25f, 4.5f), ListDrawer, collapsible: true) { data = [.. modules.overrideCrafterBeacons] };
    }

    /// <summary>
    /// Draw one item in the per-crafter beacon override display.
    /// </summary>
    private void ListDrawer(ImGui gui, KeyValuePair<EntityCrafter, BeaconOverrideConfiguration> element, int index) {
        (EntityCrafter crafter, BeaconOverrideConfiguration config) = element;
        DisplayAmount amount = config.beaconCount;
        GoodsWithAmountEvent click = gui.BuildFactorioObjectWithEditableAmount(crafter, amount, ButtonDisplayStyle.ProductionTableUnscaled, allowScroll: false,
            tooltipOptions: new() {
                DrawBelowHeader = gui => {
                    using (gui.EnterRow()) {
                        // Allocate the width now, but draw the text later so it can be vertically centered.
                        Rect rect = gui.AllocateTextRect(out _, "Affected by " + element.Value.beaconCount, TextBlockDisplayStyle.Default(SchemeColor.None));
                        gui.BuildFactorioObjectIcon(element.Value.beacon, ButtonDisplayStyle.ProductionTableUnscaled);
                        rect.Height = gui.lastRect.Height;
                        gui.DrawText(rect, "Affected by " + element.Value.beaconCount);
                        gui.BuildText("each containing " + element.Value.beacon.target.moduleSlots);
                        gui.BuildFactorioObjectIcon(element.Value.beaconModule, ButtonDisplayStyle.ProductionTableUnscaled);
                    }
                }
            });
        gui.DrawIcon(new(gui.lastRect.X, gui.lastRect.Y, 1.25f, 1.25f), config.beacon.target.icon, SchemeColor.Source);
        gui.DrawIcon(new(gui.lastRect.TopRight - new Vector2(1.25f, 0), new Vector2(1.25f, 1.25f)), config.beaconModule.target.icon, SchemeColor.Source);
        switch (click) {
            case GoodsWithAmountEvent.LeftButtonClick:
                SelectSingleObjectPanel.SelectQualityWithNone(Database.usableBeacons, "Select beacon", selectedBeacon => {

                    if (selectedBeacon is null) {
                        _ = modules.overrideCrafterBeacons.Remove(crafter);
                    }
                    else {
                        modules.overrideCrafterBeacons[crafter] = modules.overrideCrafterBeacons[crafter] with { beacon = selectedBeacon };

                        if (!selectedBeacon.target.CanAcceptModule(modules.overrideCrafterBeacons[crafter].beaconModule)) {
                            _ = Database.GetDefaultModuleFor(selectedBeacon.target, out Module? module);
                            // null-forgiving: Anything from usableBeacons accepts at least one module.
                            modules.overrideCrafterBeacons[crafter] = modules.overrideCrafterBeacons[crafter] with { beaconModule = module!.With(Quality.MaxAccessible) };
                        }
                    }

                    overrideList.data = [.. modules.overrideCrafterBeacons];
                }, modules.overrideCrafterBeacons[crafter].beacon.quality, noneTooltip: "Click here to remove the current override.");
                break;
            case GoodsWithAmountEvent.RightButtonClick:
                SelectSingleObjectPanel.SelectQualityWithNone(Database.allModules.Where(m => modules.overrideCrafterBeacons[crafter].beacon.target.CanAcceptModule(m.moduleSpecification)),
                    "Select beacon module", selectedModule => {

                        if (selectedModule is null) {
                            _ = modules.overrideCrafterBeacons.Remove(crafter);
                        }
                        else {
                            modules.overrideCrafterBeacons[crafter] = modules.overrideCrafterBeacons[crafter] with { beaconModule = selectedModule };
                        }

                        overrideList.data = [.. modules.overrideCrafterBeacons];
                    }, modules.overrideCrafterBeacons[crafter].beaconModule.quality, noneTooltip: "Click here to remove the current override.");
                break;
            case GoodsWithAmountEvent.TextEditing when amount.Value >= 0:
                modules.overrideCrafterBeacons[crafter] = modules.overrideCrafterBeacons[crafter] with { beaconCount = (int)amount.Value };
                overrideList.data = [.. modules.overrideCrafterBeacons];
                break;
        }
    }

    /// <summary>
    /// Draw the slider that controls the price of the modules that are automatically selected.
    /// </summary>
    public static void BuildSimple(ImGui gui, ModuleFillerParameters modules) {
        float payback = modules.autoFillPayback;
        float modulesLog = MathUtils.LogarithmicToLinear(payback, ModulesMinPayback, ModulesMaxPayback);
        if (gui.BuildSlider(modulesLog, out float newValue)) {
            payback = MathUtils.LinearToLogarithmic(newValue, ModulesMinPayback, ModulesMaxPayback, 0f, float.MaxValue); // JSON can't handle infinities
            modules.autoFillPayback = payback;
        }

        if (payback <= 0f) {
            gui.BuildText("Use no modules");
        }
        else if (payback >= float.MaxValue) {
            gui.BuildText("Use best modules");
        }
        else {
            gui.BuildText("Modules payback estimate: " + DataUtils.FormatTime(payback), TextBlockDisplayStyle.WrappedText);
        }
    }

    /// <summary>
    /// Draw the full configuration panel, with all the options except the slider.
    /// </summary>
    public override void Build(ImGui gui) {
        EntityBeacon? beacon = Database.usableBeacons.FirstOrDefault();
        _ = Database.GetDefaultModuleFor(beacon, out Module? defaultBeaconModule);
        IObjectWithQuality<EntityBeacon>? defaultBeacon = beacon.With(Quality.MaxAccessible);
        IObjectWithQuality<Module>? beaconFillerModule = defaultBeaconModule.With(Quality.MaxAccessible);

        BuildHeader(gui, "Module autofill parameters");
        BuildSimple(gui, modules);
        if (gui.BuildCheckBox("Fill modules in miners", modules.fillMiners, out bool newFill)) {
            modules.fillMiners = newFill;
        }

        gui.AllocateSpacing();
        gui.BuildText("Filler module:", Font.subheader);
        gui.BuildText("Use this module when autofill doesn't add anything (for example when productivity modules doesn't fit)", TextBlockDisplayStyle.WrappedText);
        if (gui.BuildFactorioObjectButtonWithText(modules.fillerModule) == Click.Left) {
            SelectSingleObjectPanel.SelectQualityWithNone(Database.allModules, "Select filler module", select => modules.fillerModule = select, modules.fillerModule?.quality);
        }

        gui.AllocateSpacing();
        gui.BuildText("Beacons & beacon modules:", Font.subheader);
        if (defaultBeacon is null || beaconFillerModule is null) {
            gui.BuildText("Your mods contain no beacons, or no modules that can be put into beacons.");
        }
        else {
            if (gui.BuildFactorioObjectButtonWithText(modules.beacon) == Click.Left) {
                SelectSingleObjectPanel.SelectQualityWithNone(Database.allBeacons, "Select beacon", select => {
                    modules.beacon = select;
                    if (modules.beaconModule != null && (modules.beacon == null || !modules.beacon.target.CanAcceptModule(modules.beaconModule))) {
                        modules.beaconModule = null;
                    }

                    gui.Rebuild();
                }, modules.beacon?.quality);
            }

            if (gui.BuildFactorioObjectButtonWithText(modules.beaconModule) == Click.Left) {
                SelectSingleObjectPanel.SelectQualityWithNone(Database.allModules.Where(x => modules.beacon?.target.CanAcceptModule(x.moduleSpecification) ?? false),
                    "Select module for beacon", select => modules.beaconModule = select, modules.beaconModule?.quality);
            }

            using (gui.EnterRow()) {
                gui.BuildText("Beacons per building: ");
                DisplayAmount amount = modules.beaconsPerBuilding;

                if (gui.BuildFloatInput(amount, TextBoxDisplayStyle.ModuleParametersTextInput) && (int)amount.Value > 0) {
                    modules.beaconsPerBuilding = (int)amount.Value;
                }
            }
            gui.BuildText("Please note that beacons themselves are not part of the calculation", TextBlockDisplayStyle.WrappedText);

            gui.AllocateSpacing();
            gui.BuildText("Override beacons:", Font.subheader);

            if (modules.overrideCrafterBeacons.Count > 0) {
                using (gui.EnterGroup(new Padding(1, 0, 0, 0))) {
                    gui.BuildText("Click to change beacon, right-click to change module", topOffset: -0.5f);
                    gui.BuildText("Select the 'none' item in either prompt to remove the override.", topOffset: -0.5f);
                }
            }
            gui.AllocateSpacing(.5f);
            overrideList.Build(gui);

            using (gui.EnterRow(allocator: RectAllocator.Center)) {
                if (gui.BuildButton("Add an override for a building type")) {
                    SelectMultiObjectPanel.Select(Database.allCrafters.Where(x => x.allowedEffects != AllowedEffects.None && !modules.overrideCrafterBeacons.ContainsKey(x)),
                        "Add exception(s) for:",
                        crafter => {
                            modules.overrideCrafterBeacons[crafter] = new BeaconOverrideConfiguration(modules.beacon ?? defaultBeacon, modules.beaconsPerBuilding,
                                modules.beaconModule ?? beaconFillerModule);
                            overrideList.data = [.. modules.overrideCrafterBeacons];
                        });
                }
            }
        }

        if (gui.BuildButton("Done")) {
            Close();
        }

        if (Project.current.settings.justChanged) {
            Project.current.RecalculateDisplayPages();
        }
    }

    protected override void ReturnPressed() => Close();
}
