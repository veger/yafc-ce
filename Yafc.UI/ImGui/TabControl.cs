using System;
using System.Collections.Generic;
using System.Linq;

namespace Yafc.UI;

/// <summary>
/// Provides a tab control. Tab controls draw a set of tabs that can be clicked and the content of the active tab.
/// The tab buttons will be split into multiple rows if necessary.
/// </summary>
public sealed class TabControl {
    private readonly TabPage[] tabPages;

    private int activePage;
    private float horizontalTextPadding = .5f;
    private float horizontalTabSeparation = .8f;
    private float maximumTextCompression = .75f;
    private float verticalTabSeparation = .25f;

    private readonly List<TabRow> rows = [];
    private float layoutWidth;
    private ImGui gui = null!;

    /// <summary>
    /// Sets the active page index. If this is not a valid index into the array of tab pages, no tab will be drawn selected, and no page content will be drawn.
    /// The tabs will still be drawn, and the user may select any tab to make it active.
    /// </summary>
    /// <param name="pageIdx">The index of the newly active page, or an invalid index if no page should be active.</param>
    public void SetActivePage(int pageIdx) => activePage = pageIdx;

    /// <summary>
    /// Gets or sets the padding between the left and right ends of the text and the left and right edges of the tab. Must not be negative.
    /// </summary>
    public float HorizontalTextPadding {
        get => horizontalTextPadding;
        set {
            if (horizontalTextPadding != value) {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                horizontalTextPadding = value;
                InvalidateLayout();
            }
        }
    }

    /// <summary>
    /// Gets or sets the blank space left between two adjacent tabs. Must not be negative.
    /// </summary>
    public float HorizontalTabSeparation {
        get => horizontalTabSeparation;
        set {
            if (horizontalTabSeparation != value) {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                horizontalTabSeparation = value;
                InvalidateLayout();
            }
        }
    }

    /// <summary>
    /// Gets or sets the smallest horizontal scale factor that may be applied to title text before creating a new row of tabs. Must not be negative or greater than 1.
    /// Smaller values permit narrower tabs. <c>1</c> means no horizontal squishing is permitted (unless the tab doesn't fit even when given in its own row),
    /// and <c>0.67f</c> allows text to be drawn using as little as 67% of its natural width.
    /// If a tab's title text does not fit on a single row, it will be compressed to fit regardless of this setting.
    /// </summary>
    public float MaximumTextCompression {
        get => maximumTextCompression;
        set {
            if (maximumTextCompression != value) {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1);
                maximumTextCompression = value;
                InvalidateLayout();
            }
        }
    }

    /// <summary>
    /// Gets or sets the vertical spacing between rows of tabs. Must not be negative.
    /// </summary>
    // Does not invalidate layout. The layout is only concerned about widths, not heights.
    public float VerticalTabSeparation {
        get => verticalTabSeparation;
        set {
            if (verticalTabSeparation != value) {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                verticalTabSeparation = value;
            }
        }
    }

    /// <summary>
    /// Gets the non-text space required for the first tab in a row.
    /// </summary>
    private float FirstTabSpacing => HorizontalTextPadding * 2;
    /// <summary>
    /// Gets the non-text space required when adding an additional tab to a row.
    /// </summary>
    private float AdditionalTabSpacing => HorizontalTabSeparation + HorizontalTextPadding * 2;

    /// <summary>
    /// Constructs a new <see cref="TabControl"/> for displaying the specified <see cref="TabPage"/>s.
    /// </summary>
    /// <param name="tabPages">An array of <see cref="TabPage"/>s to be drawn.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="tabPages"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="tabPages"/> is an empty array.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="tabPages"/> contains a <see langword="null"/> value.</exception>
    public TabControl(params TabPage[] tabPages) {
        ArgumentNullException.ThrowIfNull(tabPages);
        ArgumentOutOfRangeException.ThrowIfZero(tabPages.Length);

        if (tabPages.Contains(null)) {
            throw new ArgumentException("tabPages must not contain null.", nameof(tabPages));
        }

        // Prevent changes to the array now that we've verified it. Any writable portions of the individual pages (if/when added) can still be changed.
        // This could be used for an IsEnabled property, for example.
        this.tabPages = [.. tabPages];
    }

    // Measure the tab titles and assign the tabs to rows.
    private void PerformLayout() {
        #region Assign tab buttons to rows and calculate the horizontal squish factors.

        rows.Clear();
        layoutWidth = gui.statePosition.Width;
        float rowTextWidth = GetTitleWidth(tabPages[0]);
        float rowPaddingWidth = FirstTabSpacing;
        int nextTabToAssign = 0;

        for (int i = 1; i < tabPages.Length; i++) {
            float textWidth = GetTitleWidth(tabPages[i]);

            rowPaddingWidth += AdditionalTabSpacing;
            rowTextWidth += textWidth;

            if (rowPaddingWidth + rowTextWidth * MaximumTextCompression > layoutWidth) {
                // Adding this tab to the current row takes too much squishing. Complete the current row and add this tab to the next one.
                rowPaddingWidth -= AdditionalTabSpacing;
                rowTextWidth -= textWidth;

                rows.Add((nextTabToAssign, i, Math.Min(1, (layoutWidth - rowPaddingWidth) / rowTextWidth)));
                nextTabToAssign = i;

                rowPaddingWidth = FirstTabSpacing;
                rowTextWidth = textWidth;
            }
        }
        // Complete the final row.
        rows.Add((nextTabToAssign, tabPages.Length, Math.Min(1, (layoutWidth - rowPaddingWidth) / rowTextWidth)));

        #endregion

        // Reverse the rows so the render pass can just use ROW_HEIGHT*rowIdx to draw the tabs from bottom up. Rows end up re-numbered as:
        // 0: [Seventh] [Eighth] [Ninth]
        // 1: [Fourth] [Fifth] [Sixth]
        // 2: [First] [Second] [Third]
        rows.Reverse();
    }

    /// <summary>
    /// Call to draw this <see cref="TabControl"/> and its active page.
    /// </summary>
    public void Build(ImGui gui) {
        // Implementation note: TabPage.Drawer is permitted to be null. Accommodating a null GuiBuilder was easy and useful for testing.
        // On the other hand, there's no reason to do that in real life, so the accommodation is not acknowledged in the nullable annotations.

        this.gui = gui;

        if (gui.statePosition.Width != layoutWidth) {
            PerformLayout();
        }

        #region Draw Tabs

        // Rotate the active page to the end of the list
        if (activePage >= 0 && activePage < tabPages.Length) {
            while (rows[^1].Start > activePage || rows[^1].End <= activePage) {
                rows.Add(rows[0]);
                rows.RemoveAt(0);
            }
        }

        for (int currentRow = 0; currentRow < rows.Count; currentRow++) {
            float startingX = gui.statePosition.Left;
            float startingY = gui.statePosition.Top + (2.25f + VerticalTabSeparation) * currentRow;
            foreach (TabPage tabPage in tabPages[rows[currentRow].Range]) {
                float textWidth = GetTitleWidth(tabPage, rows[currentRow].Compression);
                Rect buttonRect = new(startingX, startingY, textWidth + HorizontalTextPadding * 2, 2.25f);
                if (Array.IndexOf(tabPages, tabPage) == activePage) {
                    // Draw the active tab as just a rectangle
                    gui.DrawRectangle(buttonRect, SchemeColor.Secondary);
                }
                // and all the other tabs as buttons
                else if (gui.BuildButton(buttonRect, SchemeColor.Primary, SchemeColor.PrimaryAlt)) {
                    activePage = Array.IndexOf(tabPages, tabPage);
                }

                Rect textRect = new(startingX + HorizontalTextPadding, buttonRect.Top, textWidth, 2.25f);
                gui.DrawText(textRect, tabPage.Title, RectAlignment.MiddleFullRow);
                startingX += buttonRect.Width + HorizontalTabSeparation;
            }

            // On every row except the last, draw a Primary bar across the bottom of the buttons, to make them look more tab-like.
            if (currentRow != rows.Count - 1) {
                float top = startingY + 2.25f;
                float bottom = top + VerticalTabSeparation / 2;
                // If we aren't using the full width, the connector stops half a HorizontalTabSeparation past the last tab button.
                // (equivalently, half a HorizontalTabSeparation before where the next button would be drawn, if it existed.)
                float right = Math.Min(startingX - HorizontalTabSeparation / 2, gui.statePosition.Right);
                gui.DrawRectangle(Rect.SideRect(gui.statePosition.Left, right, top, bottom), SchemeColor.Primary);
            }
        }

        gui.AllocateRect(0, (2.25f + VerticalTabSeparation) * rows.Count - VerticalTabSeparation + .25f, 0); // Allocate space (vertical) for the tabs
        // On the last row, draw a Secondary bar across the bottom of the buttons, to connect the active tab to the controls that will be drawn.
        // Unlike the Primary bars, this one is always full-width.
        gui.DrawRectangle(new(gui.statePosition.X, gui.statePosition.Y - .25f, layoutWidth, .25f), SchemeColor.Secondary);

        #endregion

        #region Allocate and draw tab content

        using var controller = gui.StartOverlappingAllocations(false);

        for (int i = 0; i < tabPages.Length; i++) {
            controller.StartNextAllocatePass(i == activePage);
            tabPages[i].Drawer?.Invoke(gui);
        }

        #endregion
    }

    /// <summary>
    /// Call if you make as-yet-undetected changes that require recalculating the tab widths or tab row assignments.<br/>
    /// After confirming this call fixes things, update the property setters or body of <see cref="Build"/> detect that change and re-layout without an explicit call.
    /// </summary>
    public void InvalidateLayout() => layoutWidth = 0;

    private float GetTitleWidth(TabPage tabPage, float compression = 1) => gui.GetTextDimensions(out _, tabPage.Title).X * compression;

    private record TabRow(int Start, int End, float Compression) {
        public Range Range => Start..End;

        public static implicit operator TabRow((int Start, int End, float Compression) value) => new TabRow(value.Start, value.End, value.Compression);
    }
}

/// <summary>
/// A description of a page to be drawn as part of a tab control.
/// </summary>
/// <param name="title">The text to be drawn on the tab button.</param>
/// <param name="drawer">The <see cref="GuiBuilder"/> to be called when drawing content for the tab page (if active) or allocating space (if not active).</param>
public sealed class TabPage(string title, GuiBuilder drawer) {
    /// <summary>
    /// Gets the text to be drawn in the tab button.
    /// </summary>
    public string Title { get; } = title ?? throw new ArgumentNullException(nameof(title));
    /// <summary>
    /// Gets the <see cref="GuiBuilder"/> that allocates space and draws content for the tab page.
    /// </summary>
    public GuiBuilder Drawer { get; } = drawer;

    public static implicit operator TabPage((string Title, GuiBuilder Drawer) value) => new TabPage(value.Title, value.Drawer);
}
