using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Yafc.I18n;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

/// <summary>
/// Encapsulates the options for drawing an object selection list, which may be a dropdown, a <see cref="SelectObjectPanel{TResult, TDisplay}"/>,
/// or both with the dropdown displayed first.
/// </summary>
/// <typeparam name="T">The type of <see cref="FactorioObject"/> to be selected</typeparam>
/// <param name="Header">The header text to display, or <see langword="null"/> to display no header text.</param>
/// <param name="Ordering">The sort order to use when displaying the items. Defaults to <see cref="DataUtils.DefaultOrdering"/>.</param>
/// <param name="MaxCount">The maximum number of elements in a dropdown list. If there are more, and a dropdown is being displayed, a
/// <see cref="SelectObjectPanel{TResult, TDisplay}"/> can be opened by the user to show the full list.</param>
/// <param name="Multiple">If <see langword="true"/>, the user will be allowed to select multiple items.<br/>
/// Must be <see langword="false"/> when selecting with a 'None' item.</param>
/// <param name="Checkmark">If not <see langword="null"/>, this will be called to determine if a checkmark should be drawn on the item.
/// Not used when <paramref name="Multiple"/> is <see langword="false"/>.</param>
/// <param name="YellowMark">If Checkmark is not set, draw a less distinct checkmark instead.</param>
/// <param name="ExtraText">If not <see langword="null"/>, this will be called to get extra text to be displayed right-justified in a dropdown
/// list, after the item's name. Not used when displaying <see cref="SelectObjectPanel{TResult, TDisplay}"/>s.</param>
public record ObjectSelectOptions<T>(string? Header, [AllowNull] IComparer<T> Ordering = null, int MaxCount = 6, bool Multiple = false, Predicate<T>? Checkmark = null,
    Predicate<T>? YellowMark = null, Func<T, string>? ExtraText = null) where T : class, IFactorioObjectWrapper {

    public IComparer<T> Ordering { get; init; } = Ordering ?? DataUtils.DefaultOrdering;
}

/// <summary>
/// Encapsulates the options for drawing an object selection list, which may be a dropdown, a <see cref="SelectObjectPanel{TResult, TDisplay}"/>,
/// or both with the dropdown displayed first, along with options to select the quality.<br/>
/// Also stores the state of the quality selection, for storing the state across draw cycles, reacting to quality changes, and/or reading the
/// selected qualities when an item is selected or the selection panel is closed.
/// </summary>
/// <typeparam name="T"><inheritdoc/></typeparam>
/// <param name="Header">The header text to display, or <see langword="null"/> to display no header text.</param>
/// <param name="Ordering">The sort order to use when displaying the items. Defaults to <see cref="DataUtils.DefaultOrdering"/>.</param>
/// <param name="MaxCount">The maximum number of elements in a dropdown list. If there are more and a dropdown is being displayed, a
/// <see cref="SelectObjectPanel{TResult, TDisplay}"/> can be opened by the user to show the full list.</param>
/// <param name="Multiple">If <see langword="true"/>, the user will be allowed to select multiple items and multiple qualities.<br/>
/// Must be <see langword="false"/> when selecting with a 'None' item.</param>
/// <param name="Checkmark">If not <see langword="null"/>, this will be called to determine if a checkmark should be drawn on the item.
/// Not used when <paramref name="Multiple"/> is <see langword="false"/>.</param>
/// <param name="YellowMark">If Checkmark is not set, draw a less distinct checkmark instead.</param>
/// <param name="ExtraText">If not <see langword="null"/>, this will be called to get extra text to be displayed right-justified in a dropdown
/// list, after the item's name. Not used when displaying <see cref="SelectObjectPanel{TResult, TDisplay}"/>s.</param>
/// <param name="AllowMultipleWithoutControl">When this and <paramref name="Multiple"/> are both <see langword="true"/>, the quality icons in the
/// selection list will always behave like checkboxes. When <paramref name="Multiple"/> is <see langword="true"/> and this is
/// <see langword="false"/>, the quality icons will behave like checkboxes when ctrl-clicking, and like radio buttons when clicking without
/// control. When <paramref name="Multiple"/> is <see langword="false"/>, the quality icons will always behave like radio buttons.</param>
/// <param name="SelectedQuality">The initially selected <see cref="Quality"/>. If this parameter is <see langword="null"/> and
/// <paramref name="Multiple"/> is <see langword="false"/>, <see cref="SelectedQuality"/> will be initialized to <see cref="Quality.Normal"/>.
/// </param>
/// <remarks>If the quality selections are not preserved across draw cycles, construct the QSO in the containing or calling method.</remarks>
public record QualitySelectOptions<T>(string? Header, [AllowNull] IComparer<T> Ordering = null, int MaxCount = 6, bool Multiple = false,
    Predicate<T>? Checkmark = null, Predicate<T>? YellowMark = null, Func<T, string>? ExtraText = null, bool AllowMultipleWithoutControl = false,
    Quality? SelectedQuality = null)
    : ObjectSelectOptions<T>(Header, Ordering, MaxCount, Multiple, Checkmark, YellowMark, ExtraText) where T : class, IFactorioObjectWrapper {

    /// <summary>
    /// Creates a new <see cref="QualitySelectOptions{T}"/> using just the header text and selected quality.
    /// </summary>
    /// <param name="header">The header text to display, or <see langword="null"/> to display no header text.</param>
    /// <param name="selectedQuality">The initially selected <see cref="Quality"/>. If this parameter is <see langword="null"/>,
    /// <see cref="SelectedQuality"/> will be initialized to <see cref="Quality.Normal"/>.</param>
    // (Seven of the seventeen constructor calls use only these two parameters.)
    public QualitySelectOptions(string? header, Quality? selectedQuality) : this(header, null, SelectedQuality: selectedQuality) { }

    /// <summary>
    /// The translation key for text to draw above the quality icons. By default this is <see cref="LSs.SelectQuality"/>, which will be
    /// pluralized as appropriate based on <see cref="ObjectSelectOptions{T}.Multiple"/>.
    /// </summary>
    public LocalizableString QualityHeader { get; init; } = LSs.SelectQuality;

    /// <summary>
    /// Gets the (first) quality selected and sets the only quality selected. Gets or sets <see langword="null"/> if no qualities are (or should
    /// be) selected. If the constructor parameter Multiple is <see langword="false"/> and the constructor parameter SelectedQuality is
    /// <see langword="null"/>, this property's initial value is <see cref="Quality.Normal"/>. Otherwise, its initial value is the same as the
    /// constructor parameter SelectedQuality.<br/>
    /// When <see cref="ObjectSelectOptions{T}.Multiple"/> is <see langword="false"/>, this cannot become <see langword="null"/> as a result of
    /// user action, though it may be programmatically set to <see langword="null"/>.<br/>
    /// </summary>
    public Quality? SelectedQuality {
        get => SelectedQualities.FirstOrDefault();
        set {
            SelectedQualities.Clear();
            if (value != null) {
                SelectedQualities.Add(value);
            }
        }
    }

    /// <summary>
    /// Called when <see cref="SelectedQualities"/>, and possibly <see cref="SelectedQuality"/> has changed due to user input. The parameter is
    /// the active <see cref="ImGui"/>.
    /// </summary>
    public event Action<ImGui>? SelectedQualitiesChanged;
    internal void OnSelectedQualitiesChanged(ImGui gui) => SelectedQualitiesChanged?.Invoke(gui);

    /// <summary>
    /// Gets all qualities selected by the user. If no qualities are selected (equivalently, if <see cref="SelectedQuality"/> is
    /// <see langword="null"/>), this is an empty list.
    /// </summary>
    public List<Quality> SelectedQualities { get; } = SelectedQuality == null ? Multiple ? [] : [Quality.Normal] : [SelectedQuality];
}

public static class QualityOptionsExtensions {
    /// <summary>
    /// Applies the <see cref="QualitySelectOptions{T}.SelectedQualities"/> to <paramref name="obj"/>. If no qualities are selected, nothing is
    /// returned. Otherwise, a quality-immune <typeparamref name="T"/> is returned once in normal quality, and a quality-compatible
    /// <typeparamref name="T"/> is returned once for each selected quality.
    /// returned only once, in normal quality, while quality aware</summary>
    /// <param name="obj">The object to combine with the currently selected qualities.</param>
    /// <returns>A sequence of <see cref="IObjectWithQuality{T}"/>s containing each valid combination of <paramref name="obj"/> and a  selected
    /// <see cref="Quality"/>.</returns>
    public static IEnumerable<IObjectWithQuality<T>> ApplyQualitiesTo<T>(this QualitySelectOptions<T> options, T obj) where T : FactorioObject {
        bool addedSomething = false;
        foreach (var quality in options.SelectedQualities) {
            if (obj.With(quality).quality == quality) {
                addedSomething = true;
                yield return obj.With(quality);
            }
        }

        if (!addedSomething) {
            if (options.SelectedQualities.Count > 0) {
                // If the selected object is normal-only and normal quality wasn't selected, return it anyway.
                yield return obj.With(Quality.Normal);
            }
        }
    }
}
