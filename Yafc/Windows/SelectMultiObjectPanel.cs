using System;
using System.Collections.Generic;
using System.Numerics;
using Yafc.I18n;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

public class SelectMultiObjectPanel : SelectObjectPanel<IEnumerable<FactorioObject>> {
    private readonly HashSet<FactorioObject> results = [];
    private readonly Predicate<FactorioObject> checkMark;
    private readonly Predicate<FactorioObject> yellowMark;
    private bool allowAutoClose = true;

    private SelectMultiObjectPanel(Predicate<FactorioObject> checkMark, Predicate<FactorioObject> yellowMark) {
        this.checkMark = checkMark;
        this.yellowMark = yellowMark;
    }

    /// <summary>
    /// Opens a <see cref="SelectMultiObjectPanel"/> to allow the user to select one or more <see cref="FactorioObject"/>s.
    /// </summary>
    /// <param name="list">The items to be displayed in this panel.</param>
    /// <param name="options">The <see cref="ObjectSelectOptions{T}"/> controlling the appearance and function of this panel.</param>
    /// <param name="selectItem">An action to be called for each selected item when the panel is closed.</param>
    public static void Select<T>(IEnumerable<T> list, ObjectSelectOptions<T> options, Action<T> selectItem) where T : FactorioObject {
        ThrowIfSingle(options);

        SelectMultiObjectPanel panel = new(o => options.Checkmark?.Invoke((T)o) ?? false, o => options.YellowMark?.Invoke((T)o) ?? false); // This casting is messy, but pushing T all the way around the call stack and type tree was messier.
        panel.Select(list, false, options, selectItem!, (objs, mappedAction) => { // null-forgiving: selectItem will not be called with null, because allowNone is false.
            foreach (var obj in objs!) { // null-forgiving: mapResult will not be called with null, because allowNone is false.
                mappedAction(obj);
            }
        });
    }

    /// <summary>
    /// Opens a <see cref="SelectMultiObjectPanel"/> to allow the user to select one or more <see cref="FactorioObject"/>s.
    /// </summary>
    /// <param name="list">The items to be displayed in this panel.</param>
    /// <param name="options">The <see cref="ObjectSelectOptions{T}"/> controlling the appearance and function of this panel.</param>
    /// <param name="selectItem">An action to be called for each selected item when the panel is closed.</param>
    public static void SelectWithQuality<T>(IEnumerable<T> list, ObjectSelectOptions<T> options, Quality currentQuality,
        Action<IObjectWithQuality<T>> selectItem) where T : FactorioObject {

        ThrowIfSingle(options);

        SelectMultiObjectPanel panel = new(o => options.Checkmark?.Invoke((T)o) ?? false, o => options.YellowMark?.Invoke((T)o) ?? false); // This casting is messy, but pushing T all the way around the call stack and type tree was messier.
        panel.SelectWithQuality(list, false, options, selectItem!, (objs, mappedAction) => { // null-forgiving: selectItem will not be called with null, because allowNone is false.
            foreach (var obj in objs!) { // null-forgiving: mapResult will not be called with null, because allowNone is false.
                mappedAction(obj);
            }
        }, null, currentQuality);
    }

    private static void ThrowIfSingle<T>(ObjectSelectOptions<T> options) where T : FactorioObject {
        if (!options.Multiple) {
            throw new ArgumentException($"Cannot open a {nameof(SelectMultiObjectPanel)} with {nameof(options)}.{nameof(ObjectSelectOptions<T>.Multiple)} set to false.", nameof(options));
        }
    }

    protected override void NonNullElementDrawer(ImGui gui, FactorioObject element) {
        SchemeColor bgColor = results.Contains(element) ? SchemeColor.Primary : SchemeColor.None;
        Click click = gui.BuildFactorioObjectButton(element, ButtonDisplayStyle.SelectObjectPanel(bgColor), new() { ShowTypeInHeader = showTypeInHeader });

        if (checkMark(element)) {
            gui.DrawIcon(Rect.SideRect(gui.lastRect.TopLeft + new Vector2(1, 0), gui.lastRect.BottomRight - new Vector2(0, 1)), Icon.Check, SchemeColor.Green);
        }
        else if (yellowMark(element)) {
            gui.DrawIcon(Rect.SideRect(gui.lastRect.TopLeft + new Vector2(1, 0), gui.lastRect.BottomRight - new Vector2(0, 1)), Icon.Check, SchemeColor.TagColorYellowText);
        }

        if (click == Click.Left) {
            if (!results.Add(element)) {
                _ = results.Remove(element);
            }
            if (!InputSystem.Instance.control && allowAutoClose) {
                CloseWithResult(results);
            }
            allowAutoClose = false;
        }
    }

    public override void Build(ImGui gui) {
        base.Build(gui);
        using (gui.EnterGroup(default, RectAllocator.Center)) {
            if (gui.BuildButton(LSs.Ok)) {
                CloseWithResult(results);
            }
            gui.BuildText(LSs.SelectMultipleObjectsHint, TextBlockDisplayStyle.HintText);
        }
    }

    protected override void ReturnPressed() => CloseWithResult(results);
}
