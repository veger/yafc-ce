using System;
using System.Collections.Generic;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

/// <summary>
/// Represents a panel that can generate a result by selecting zero or one <see cref="FactorioObject"/>s. (But doesn't have to, if the user selects a close or cancel button.)
/// </summary>
public static class SelectSingleObjectPanel {
    /// <summary>
    /// Opens a <see cref="SelectObjectPanel{TResult, TDisplay}"/> to allow the user to select one or more <see cref="FactorioObject"/>s.
    /// </summary>
    /// <param name="list">The items to be displayed in this panel.</param>
    /// <param name="options">The <see cref="ObjectSelectOptions{T}"/> controlling the appearance and function of this panel.</param>
    /// <param name="selectItem">An action to be called for the selected item when the panel is closed.</param>
    public static void Select<T>(IEnumerable<T> list, ObjectSelectOptions<T> options, Action<T> selectItem) where T : FactorioObject {
        ThrowIfMultiple(options);

        Panel<T>.Select(list, options, selectItem);
    }

    /// <summary>
    /// Opens a <see cref="SelectObjectPanel{TResult, TDisplay}"/> to allow the user to select one or more <see cref="FactorioObject"/>s, along
    /// with selecting the quality for those objects.
    /// </summary>
    /// <param name="list">The items to be displayed in this panel.</param>
    /// <param name="options">The <see cref="ObjectSelectOptions{T}"/> controlling the appearance and function of this panel.</param>
    /// <param name="selectItem">An action to be called for the selected item when the panel is closed.</param>
    public static void SelectWithQuality<T>(IEnumerable<T> list, QualitySelectOptions<T> options, Action<IObjectWithQuality<T>> selectItem)
        where T : FactorioObject {
        ThrowIfMultiple(options);

        Panel<T>.SelectWithQuality(list, options, selectItem);
    }

    /// <summary>
    /// Opens a <see cref="SelectObjectPanel{TResult, TDisplay}"/> to allow the user to select one <see cref="FactorioObject"/>, or to clear the
    /// current selection by selecting an extra "none" or "clear" option.
    /// </summary>
    /// <param name="list">The items to be displayed in this panel.</param>
    /// <param name="options">The <see cref="ObjectSelectOptions{T}"/> controlling the appearance and function of this panel.</param>
    /// <param name="selectItem">An action to be called for the selected item when the panel is closed.
    /// The parameter will be <see langword="null"/> if the "none" or "clear" option is selected.</param>
    /// <param name="noneTooltip">If not <see langword="null"/>, this tooltip will be displayed when hovering over the "none" item.</param>
    /// <exception cref="ArgumentException">Thrown if <see cref="ObjectSelectOptions{T}.Multiple"/> is <see langword="true"/>.</exception>
    public static void SelectWithNone<T>(IEnumerable<T> list, ObjectSelectOptions<T> options, Action<T?> selectItem, string? noneTooltip = null)
        where T : FactorioObject {
        ThrowIfMultiple(options);

        Panel<T>.SelectWithNone(list, options, selectItem, noneTooltip);
    }

    /// <summary>
    /// Opens a <see cref="SelectObjectPanel{TResult, TDisplay}"/> to allow the user to select one <see cref="FactorioObject"/>, along
    /// with selecting the quality for that object; or to clear the current selection by selecting an extra "none" or "clear" option.
    /// </summary>
    /// <param name="list">The items to be displayed in this panel.</param>
    /// <param name="options">The <see cref="QualitySelectOptions{T}"/> controlling the appearance and function of this panel.</param>
    /// <param name="selectItem">An action to be called for the selected item when the panel is closed.
    /// The parameter will be <see langword="null"/> if the "none" or "clear" option is selected.</param>
    /// <param name="noneTooltip">If not <see langword="null"/>, this tooltip will be displayed when hovering over the "none" item.</param>
    /// <exception cref="ArgumentException">Thrown if <see cref="ObjectSelectOptions{T}.Multiple"/> is <see langword="true"/>.</exception>
    public static void SelectQualityWithNone<T>(IEnumerable<T> list, QualitySelectOptions<T> options, Action<IObjectWithQuality<T>?> selectItem,
        string? noneTooltip = null) where T : FactorioObject {
        ThrowIfMultiple(options);

        Panel<T>.SelectQualityWithNone(list, options, selectItem, noneTooltip);
    }

    private static void ThrowIfMultiple<T>(ObjectSelectOptions<T> options) where T : FactorioObject {
        if (options.Multiple) {
            throw new ArgumentException($"Cannot open a {nameof(SelectSingleObjectPanel)} with {nameof(options)}.{nameof(ObjectSelectOptions<T>.Multiple)} set to true.", nameof(options));
        }
    }

    /// <summary>
    /// The actual panel that is displayed by <see cref="SelectSingleObjectPanel"/>'s static methods
    /// </summary>
    private sealed class Panel<T> : SelectObjectPanel<T, T> where T : FactorioObject {
        public static readonly Panel<T> Instance = new Panel<T>();

        public static void Select(IEnumerable<T> list, ObjectSelectOptions<T> options, Action<T> selectItem)
            // null-forgiving: selectItem will not be called with null, because allowNone is false.
            => Instance.Select(list, false, options, selectItem!, (obj, mappedAction) => mappedAction(obj));

        public static void SelectWithQuality(IEnumerable<T> list, QualitySelectOptions<T> options, Action<IObjectWithQuality<T>> selectItem)
            // null-forgiving: selectItem will not be called with null, because allowNone is false.
            => Instance.SelectWithQuality(list, false, options, selectItem!, (obj, mappedAction) => mappedAction(obj), null);

        public static void SelectWithNone(IEnumerable<T> list, ObjectSelectOptions<T> options, Action<T?> selectItem, string? noneTooltip = null)
            => Instance.Select(list, true, options, selectItem, (obj, mappedAction) => mappedAction(obj), noneTooltip: noneTooltip);

        public static void SelectQualityWithNone(IEnumerable<T> list, QualitySelectOptions<T> options, Action<IObjectWithQuality<T>?> selectItem,
            string? noneTooltip = null)
            => Instance.SelectWithQuality(list, true, options, selectItem, (obj, mappedAction) => mappedAction(obj), noneTooltip);

        protected override void NonNullElementDrawer(ImGui gui, T element) {
            if (gui.BuildFactorioObjectButton(element, ButtonDisplayStyle.SelectObjectPanel(SchemeColor.None),
                tooltipOptions: new() { ShowTypeInHeader = showTypeInHeader }) == Click.Left) {

                CloseWithResult(element);
            }
        }
    }
}
