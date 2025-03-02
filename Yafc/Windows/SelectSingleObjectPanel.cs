using System;
using System.Collections.Generic;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

/// <summary>
/// Represents a panel that can generate a result by selecting zero or one <see cref="FactorioObject"/>s. (But doesn't have to, if the user selects a close or cancel button.)
/// </summary>
public class SelectSingleObjectPanel : SelectObjectPanel<FactorioObject> {
    private static readonly SelectSingleObjectPanel Instance = new SelectSingleObjectPanel();
    public SelectSingleObjectPanel() : base() { }

    /// <summary>
    /// Opens a <see cref="SelectSingleObjectPanel"/> to allow the user to select one <see cref="FactorioObject"/>.
    /// </summary>
    /// <param name="list">The items to be displayed in this panel.</param>
    /// <param name="options">The <see cref="ObjectSelectOptions{T}"/> controlling the appearance and function of this panel.</param>
    /// <param name="selectItem">An action to be called for the selected item when the panel is closed.</param>
    public static void Select<T>(IEnumerable<T> list, ObjectSelectOptions<T> options, Action<T> selectItem) where T : FactorioObject {
        ThrowIfMultiple(options);

        // null-forgiving: selectItem will not be called with null, because allowNone is false.
        Instance.Select(list, false, options, selectItem!, (obj, mappedAction) => mappedAction(obj));
    }

    /// <summary>
    /// Opens a <see cref="SelectSingleObjectPanel"/> to allow the user to select one <see cref="FactorioObject"/>, or to clear the current selection by selecting
    /// an extra "none" or "clear" option.
    /// </summary>
    /// <param name="list">The items to be displayed in this panel.</param>
    /// <param name="options">The <see cref="ObjectSelectOptions{T}"/> controlling the appearance and function of this panel.</param>
    /// <param name="selectItem">An action to be called for the selected item when the panel is closed.
    /// The parameter will be <see langword="null"/> if the "none" or "clear" option is selected.</param>
    public static void SelectWithQuality<T>(IEnumerable<T> list, ObjectSelectOptions<T> options, Quality? currentQuality,
        Action<IObjectWithQuality<T>> selectItem) where T : FactorioObject {
        ThrowIfMultiple(options);

        // null-forgiving: selectItem will not be called with null, because allowNone is false.
        Instance.SelectWithQuality(list, false, options, selectItem!, (obj, mappedAction) => mappedAction(obj), null, currentQuality);
    }

    /// <summary>
    /// Opens a <see cref="SelectSingleObjectPanel"/> to allow the user to select one <see cref="FactorioObject"/>, or to clear the current selection by selecting
    /// an extra "none" or "clear" option.
    /// </summary>
    /// <param name="list">The items to be displayed in this panel.</param>
    /// <param name="options">The <see cref="ObjectSelectOptions{T}"/> controlling the appearance and function of this panel.</param>
    /// <param name="selectItem">An action to be called for the selected item when the panel is closed.
    /// The parameter will be <see langword="null"/> if the "none" or "clear" option is selected.</param>
    /// <param name="noneTooltip">If not <see langword="null"/>, this tooltip will be displayed when hovering over the "none" item.</param>
    public static void SelectWithNone<T>(IEnumerable<T> list, ObjectSelectOptions<T> options, Action<T?> selectItem, string? noneTooltip = null) where T : FactorioObject {
        ThrowIfMultiple(options);

        Instance.Select(list, true, options, selectItem, (obj, mappedAction) => mappedAction(obj), noneTooltip: noneTooltip);
    }

    /// <summary>
    /// Opens a <see cref="SelectSingleObjectPanel"/> to allow the user to select one <see cref="FactorioObject"/>, or to clear the current selection by selecting
    /// an extra "none" or "clear" option.
    /// </summary>
    /// <param name="list">The items to be displayed in this panel.</param>
    /// <param name="options">The <see cref="ObjectSelectOptions{T}"/> controlling the appearance and function of this panel.</param>
    /// <param name="selectItem">An action to be called for the selected item when the panel is closed.
    /// The parameter will be <see langword="null"/> if the "none" or "clear" option is selected.</param>
    /// <param name="noneTooltip">If not <see langword="null"/>, this tooltip will be displayed when hovering over the "none" item.</param>
    public static void SelectQualityWithNone<T>(IEnumerable<T> list, ObjectSelectOptions<T> options, Quality? currentQuality,
        Action<IObjectWithQuality<T>?> selectItem, string? noneTooltip = null) where T : FactorioObject {
        ThrowIfMultiple(options);

        Instance.SelectWithQuality(list, true, options, selectItem, (obj, mappedAction) => mappedAction(obj), noneTooltip, currentQuality);
    }

    private static void ThrowIfMultiple<T>(ObjectSelectOptions<T> options) where T : FactorioObject {
        if (options.Multiple) {
            throw new ArgumentException($"Cannot open a {nameof(SelectSingleObjectPanel)} with {nameof(options)}.{nameof(ObjectSelectOptions<T>.Multiple)} set to true.", nameof(options));
        }
    }

    protected override void NonNullElementDrawer(ImGui gui, FactorioObject element) {
        if (gui.BuildFactorioObjectButton(element, ButtonDisplayStyle.SelectObjectPanel(SchemeColor.None), tooltipOptions: new() { ShowTypeInHeader = showTypeInHeader }) == Click.Left) {
            CloseWithResult(element);
        }
    }
}
