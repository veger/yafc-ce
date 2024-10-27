using System;
using System.Numerics;

namespace Yafc.UI;

public partial class ImGui {
    private CopyableState state;
    public Rect lastRect { get; set; }
    public Rect lastContentRect { get; set; }
    public float width => state.right - state.left;
    public Rect statePosition => new Rect(state.left, state.top, width, 0f);
    public ref RectAllocator allocator => ref state.allocator;
    public ref float spacing => ref state.spacing;
    public Rect layoutRect => new Rect(state.left, state.top, state.right - state.left, state.bottom - state.top);

    private void ResetLayout() {
        state = default;
        lastRect = default;
        state.right = buildWidth;
        state.spacing = 0.5f;
    }

    public void AllocateSpacing(float spacing) => _ = AllocateRect(0f, 0f, spacing);

    public void AllocateSpacing() => AllocateSpacing(state.spacing);

    public Rect AllocateRect(float width, float height, float spacing = float.NegativeInfinity) {
        var rect = state.AllocateRect(width, height, spacing);
        lastRect = state.EncapsulateRect(rect);

        return lastRect;
    }

    public Rect EncapsulateRect(Rect rect) {
        lastRect = state.EncapsulateRect(rect);

        return lastRect;
    }

    public Rect AllocateRect(float width, float height, RectAlignment alignment, float spacing = float.NegativeInfinity) {
        var bigRect = AllocateRect(width, height, spacing);
        if (alignment == RectAlignment.Full || allocator == RectAllocator.Center || allocator == RectAllocator.LeftAlign || allocator == RectAllocator.RightAlign) {
            return bigRect;
        }

        return AlignRect(bigRect, alignment, width, height);
    }

    public static Rect AlignRect(Rect boundary, RectAlignment alignment, float width, float height) => alignment switch {
        RectAlignment.Middle => new Rect(boundary.X + ((boundary.Width - width) * 0.5f), boundary.Y + ((boundary.Height - height) * 0.5f), width, height),
        RectAlignment.MiddleLeft => new Rect(boundary.X, boundary.Y + ((boundary.Height - height) * 0.5f), width, height),
        RectAlignment.MiddleRight => new Rect(boundary.X, boundary.Y + ((boundary.Height - height) * 0.5f), width, height),
        RectAlignment.UpperCenter => new Rect(boundary.X + ((boundary.Width - width) * 0.5f), boundary.Y, width, height),
        RectAlignment.MiddleFullRow => new Rect(boundary.X, boundary.Y + ((boundary.Height - height) * 0.5f), boundary.Width, height),
        _ => boundary,
    };

    public ImGui RemainingRow(float spacing = float.NegativeInfinity) {
        state.AllocateSpacing(spacing);
        allocator = RectAllocator.RemainingRow;

        return this;
    }

    public Context EnterGroup(Padding padding, RectAllocator allocator, SchemeColor textColor = SchemeColor.None, float spacing = float.NegativeInfinity) {
        state.AllocateSpacing();
        Context ctx = new Context(this, padding);
        state.allocator = allocator;

        if (!float.IsNegativeInfinity(spacing)) {
            state.spacing = spacing;
        }

        if (textColor != SchemeColor.None) {
            state.textColor = textColor;
        }

        return ctx;
    }

    public Context EnterGroup(Padding padding, SchemeColor textColor = SchemeColor.None) => EnterGroup(padding, allocator, textColor);

    public Context EnterRow(float spacing = 0.5f, RectAllocator allocator = RectAllocator.LeftRow, SchemeColor textColor = SchemeColor.None) => EnterGroup(default, allocator, textColor, spacing);

    public Context EnterFixedPositioning(float width, float height, Padding padding, SchemeColor textColor = SchemeColor.None) {
        Context context = new Context(this, padding);
        var rect = AllocateRect(width, height);
        state.left = rect.X;
        state.right = rect.Right;
        state.bottom = state.top = rect.Top;
        state.allocator = RectAllocator.Stretch;

        if (textColor != SchemeColor.None) {
            state.textColor = textColor;
        }

        return context;
    }

    /// <summary>
    /// Call to initialize drawing operations for UI elements that behave like the body of a tab control, with the additional behavior that the control is always large
    /// enough for its largest content.
    /// </summary>
    /// <param name="alsoDraw">If <see langword="true"/>, subsequent drawing commands (for the first tab page) will both allocate and draw.
    /// If <see langword="false"/>, subsequent drawing commands (for the first tab page) will allocate space, but will not draw their controls.</param>
    /// <returns>An <see cref="OverlappingAllocations"/> object that controls the allocation behavior.</returns>
    /// <remarks>After drawing each page, call <see cref="OverlappingAllocations.StartNextAllocatePass"/> to return to the top of the drawing area and start allocating
    /// (and drawing, if appropriate) the next set of controls.</remarks>
    public OverlappingAllocations StartOverlappingAllocations(bool alsoDraw) => new OverlappingAllocations(this, alsoDraw);

    private struct CopyableState {
        public RectAllocator allocator;
        public float left, right, top, bottom;
        public Rect contextRect;
        public float spacing;
        public bool hasContent;
        public SchemeColor textColor;

        public Rect AllocateRect(float width, float height, float spacing) {
            AllocateSpacing(spacing);

            if (allocator != RectAllocator.LeftRow) {
                width = Math.Min(width, right - left);
            }

            float rowHeight = MathF.Max(height, bottom - top);

            return allocator switch {
                RectAllocator.Stretch => new Rect(left, top, right - left, height),
                RectAllocator.LeftAlign => new Rect(left, top, width, height),
                RectAllocator.RightAlign => new Rect(right - width, top, width, height),
                RectAllocator.Center => new Rect((right + left - width) * 0.5f, top, width, height),
                RectAllocator.LeftRow => new Rect(left, top, width, rowHeight),
                RectAllocator.RightRow => new Rect(right - width, top, width, rowHeight),
                RectAllocator.RemainingRow => new Rect(left, top, right - left, rowHeight),
                RectAllocator.FixedRect => new Rect(left, top, right - left, rowHeight),
                RectAllocator.HalfRow => new Rect(left, top, (right - left - spacing) / 2f, rowHeight),
                _ => throw new ArgumentOutOfRangeException("Rectangle allocator is out of range with value " + allocator),
            };
        }

        public void AllocateSpacing(float amount = float.NegativeInfinity) {
            if (!hasContent) {
                return;
            }

            if (float.IsNegativeInfinity(amount)) {
                amount = spacing;
            }

            switch (allocator) {
                case RectAllocator.Stretch:
                case RectAllocator.LeftAlign:
                case RectAllocator.RightAlign:
                case RectAllocator.Center:
                    top += amount;
                    bottom = top;
                    break;
                case RectAllocator.LeftRow:
                    left += amount;
                    break;
                case RectAllocator.RightRow:
                    right -= amount;
                    break;
            }
        }

        public Rect EncapsulateRect(Rect rect) {
            contextRect = hasContent ? Rect.Union(contextRect, rect) : rect;
            hasContent = true;

            switch (allocator) {
                case RectAllocator.Stretch:
                    top = bottom = MathF.Max(rect.Bottom, top);
                    rect.X = left;
                    rect.Width = right - left;
                    break;
                case RectAllocator.RightAlign:
                    top = bottom = MathF.Max(rect.Bottom, top);
                    rect.Right = right;
                    break;
                case RectAllocator.LeftAlign:
                    top = bottom = MathF.Max(rect.Bottom, top);
                    rect.Left = left;
                    break;
                case RectAllocator.Center:
                    top = bottom = MathF.Max(rect.Bottom, top);
                    break;
                case RectAllocator.LeftRow:
                    left = rect.Right;
                    bottom = MathF.Max(rect.Bottom, bottom);
                    break;
                case RectAllocator.RightRow:
                    right = rect.Left;
                    bottom = MathF.Max(rect.Bottom, bottom);
                    break;
                case RectAllocator.HalfRow:
                    allocator = RectAllocator.RemainingRow;
                    left = rect.Right + spacing;
                    bottom = MathF.Max(rect.Bottom, bottom);
                    break;
            }

            return rect;
        }
    }

    public readonly struct Context : IDisposable {
        private readonly ImGui gui;
        private readonly CopyableState state;
        private readonly Padding padding;

        public Context(ImGui gui, Padding padding) {
            this.gui = gui;
            this.padding = padding;
            ref var cstate = ref gui.state;
            state = cstate;
            cstate.contextRect = default;
            cstate.hasContent = false;
            cstate.left += padding.left;
            cstate.right -= padding.right;
            cstate.top += padding.top;
            cstate.bottom -= padding.bottom;
        }

        public void Dispose() {
            if (gui == null) {
                return;
            }

            var rect = gui.state.contextRect;
            bool hasContent = gui.state.hasContent;
            gui.state = state;
            rect.X -= padding.left;
            rect.Y -= padding.top;
            rect.Width += padding.left + padding.right;
            rect.Height += padding.top + padding.bottom;

            if (hasContent) {
                gui.lastRect = gui.state.EncapsulateRect(rect);
                gui.lastContentRect = rect;
            }
            else {
                gui.lastRect = default;
            }
        }

        public void SetManualRect(Rect rect, RectAllocator allocator = RectAllocator.FixedRect) {
            rect += new Vector2(state.left, state.top);
            gui.spacing = 0f;
            SetManualRectRaw(rect, allocator);
        }

        public void SetWidth(float width) => gui.state.right = gui.state.left + width;

        public void SetManualRectRaw(Rect rect, RectAllocator allocator = RectAllocator.FixedRect) {
            ref var cstate = ref gui.state;
            cstate.left = rect.X + padding.left;
            cstate.right = cstate.left + rect.Width;
            cstate.top = rect.Y + padding.top;
            cstate.bottom = cstate.top + rect.Height;
            cstate.allocator = allocator;
        }
    }

    public sealed class OverlappingAllocations : IDisposable {
        private readonly ImGui gui;
        private readonly bool initialDrawState;
        private readonly float initialTop;
        private float maximumBottom;

        internal OverlappingAllocations(ImGui gui, bool alsoDraw) {
            this.gui = gui;
            initialDrawState = gui.enableDrawing;
            initialTop = gui.state.top;
            gui.enableDrawing = initialDrawState && alsoDraw;
        }

        /// <summary>
        /// Call after allocating/drawing one "page" of the tab controls, to move the draw point back to the top of the tab page and set whether the next set of controls should appear.
        /// </summary>
        /// <param name="alsoDraw">If <see langword="true"/>, subsequent drawing commands (for the next tab page) will both allocate and draw.
        /// If <see langword="false"/>, subsequent drawing commands (for the next tab page) will allocate space, but will not draw their controls.</param>
        public void StartNextAllocatePass(bool alsoDraw) {
            gui.enableDrawing = initialDrawState && alsoDraw;
            maximumBottom = Math.Max(gui.state.top, maximumBottom);
            gui.state.top = initialTop;
        }

        public void Dispose() {
            gui.enableDrawing = initialDrawState;
            gui.state.top = maximumBottom;
        }
    }

    public void SetMinWidth(float width) {
        if (width > buildingWidth) {
            buildingWidth = width;
        }
    }

    public void SetContextRect(Rect rect) {
        state.hasContent = true;
        state.contextRect = rect;
    }
}
