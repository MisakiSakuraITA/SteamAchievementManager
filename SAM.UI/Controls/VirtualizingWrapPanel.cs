/* Copyright (c) 2024 Rick (rick 'at' gibbed 'dot' us)
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using NotifyAction = System.Collections.Specialized.NotifyCollectionChangedAction;
using System.Windows.Media;

namespace SAM.UI.Controls
{
    /// <summary>
    /// A wrap panel that only realises the containers it can actually show.
    /// </summary>
    /// <remarks>
    /// WPF ships a virtualizing stack panel but no wrapping equivalent, and a plain
    /// <see cref="WrapPanel"/> inside an items control realises a container for every item --
    /// several thousand of them for a large Steam library. Items here are a uniform
    /// <see cref="ItemWidth"/> by <see cref="ItemHeight"/>, which is what makes the row
    /// arithmetic exact and lets the panel scroll to any offset without measuring the items
    /// in between.
    /// </remarks>
    public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
    {
        private const double _ScrollLineDelta = 16.0;
        private const double _MouseWheelDelta = 48.0;

        public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
            nameof(ItemWidth),
            typeof(double),
            typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(
                128.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
            nameof(ItemHeight),
            typeof(double),
            typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(
                128.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public static readonly DependencyProperty StretchItemsProperty = DependencyProperty.Register(
            nameof(StretchItems),
            typeof(bool),
            typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(
                true,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        private Size _Extent;
        private Size _Viewport;
        private Point _Offset;

        private int _ColumnCount = 1;
        private double _EffectiveItemWidth = 1.0;

        public double ItemWidth
        {
            get => (double)this.GetValue(ItemWidthProperty);
            set => this.SetValue(ItemWidthProperty, value);
        }

        public double ItemHeight
        {
            get => (double)this.GetValue(ItemHeightProperty);
            set => this.SetValue(ItemHeightProperty, value);
        }

        /// <summary>
        /// Whether to share the leftover width of each row between its items.
        /// </summary>
        /// <remarks>
        /// <see cref="ItemWidth"/> is a minimum when this is on. Without it a row only ever
        /// uses a whole multiple of the item width, so resizing the window leaves a ragged
        /// gap of up to one full column down the right-hand side.
        /// </remarks>
        public bool StretchItems
        {
            get => (bool)this.GetValue(StretchItemsProperty);
            set => this.SetValue(StretchItemsProperty, value);
        }

        #region IScrollInfo

        public bool CanHorizontallyScroll { get; set; }

        public bool CanVerticallyScroll { get; set; }

        public double ExtentWidth => this._Extent.Width;

        public double ExtentHeight => this._Extent.Height;

        public double ViewportWidth => this._Viewport.Width;

        public double ViewportHeight => this._Viewport.Height;

        public double HorizontalOffset => this._Offset.X;

        public double VerticalOffset => this._Offset.Y;

        public ScrollViewer ScrollOwner { get; set; }

        public void LineUp() => this.SetVerticalOffset(this._Offset.Y - _ScrollLineDelta);

        public void LineDown() => this.SetVerticalOffset(this._Offset.Y + _ScrollLineDelta);

        public void LineLeft() => this.SetHorizontalOffset(this._Offset.X - _ScrollLineDelta);

        public void LineRight() => this.SetHorizontalOffset(this._Offset.X + _ScrollLineDelta);

        public void PageUp() => this.SetVerticalOffset(this._Offset.Y - this._Viewport.Height);

        public void PageDown() => this.SetVerticalOffset(this._Offset.Y + this._Viewport.Height);

        public void PageLeft() => this.SetHorizontalOffset(this._Offset.X - this._Viewport.Width);

        public void PageRight() => this.SetHorizontalOffset(this._Offset.X + this._Viewport.Width);

        public void MouseWheelUp() => this.SetVerticalOffset(this._Offset.Y - _MouseWheelDelta);

        public void MouseWheelDown() => this.SetVerticalOffset(this._Offset.Y + _MouseWheelDelta);

        public void MouseWheelLeft() => this.SetHorizontalOffset(this._Offset.X - _MouseWheelDelta);

        public void MouseWheelRight() => this.SetHorizontalOffset(this._Offset.X + _MouseWheelDelta);

        public void SetHorizontalOffset(double offset)
        {
            // The panel wraps, so there is never anything to scroll to horizontally.
            if (this._Offset.X.Equals(0.0) == false)
            {
                this._Offset.X = 0;
                this.InvalidateMeasure();
            }
        }

        public void SetVerticalOffset(double offset)
        {
            var clamped = Clamp(offset, 0, Math.Max(0, this._Extent.Height - this._Viewport.Height));
            if (this._Offset.Y.Equals(clamped) == true)
            {
                return;
            }

            this._Offset.Y = clamped;
            this.InvalidateMeasure();
        }

        /// <summary>
        /// Scrolls the row holding <paramref name="visual"/> into view. Keyboard navigation
        /// through the items control lands here.
        /// </summary>
        public Rect MakeVisible(Visual visual, Rect rectangle)
        {
            var child = this.FindDirectChild(visual);
            if (child == null)
            {
                return rectangle;
            }

            var index = this.IndexFromContainer(child);
            if (index < 0)
            {
                return rectangle;
            }

            var row = index / Math.Max(1, this._ColumnCount);
            var itemHeight = this.ResolvedItemHeight;
            var top = row * itemHeight;
            var bottom = top + itemHeight;

            if (top < this._Offset.Y)
            {
                this.SetVerticalOffset(top);
            }
            else if (bottom > this._Offset.Y + this._Viewport.Height)
            {
                this.SetVerticalOffset(bottom - this._Viewport.Height);
            }

            return new(0, top - this._Offset.Y, this._EffectiveItemWidth, itemHeight);
        }

        #endregion

        private double ResolvedItemWidth => Math.Max(1.0, this.ItemWidth);

        private double ResolvedItemHeight => Math.Max(1.0, this.ItemHeight);

        protected override Size MeasureOverride(Size availableSize)
        {
            var itemCount = this.GetItemCount();
            var itemWidth = this.ResolvedItemWidth;
            var itemHeight = this.ResolvedItemHeight;

            var width = double.IsInfinity(availableSize.Width) == true
                ? itemWidth
                : availableSize.Width;
            var height = double.IsInfinity(availableSize.Height) == true
                ? itemHeight
                : availableSize.Height;

            this._ColumnCount = Math.Max(1, (int)Math.Floor(width / itemWidth));
            this._EffectiveItemWidth = this.StretchItems == true && double.IsInfinity(availableSize.Width) == false
                ? width / this._ColumnCount
                : itemWidth;

            var rowCount = itemCount == 0 ? 0 : (itemCount + this._ColumnCount - 1) / this._ColumnCount;

            var extent = new Size(width, rowCount * itemHeight);
            var viewport = new Size(width, height);

            if (this.UpdateScrollInfo(extent, viewport) == true)
            {
                this.ScrollOwner?.InvalidateScrollInfo();
            }

            this.RealizeVisibleRange(itemCount, itemHeight, new Size(this._EffectiveItemWidth, itemHeight));

            return new(
                double.IsInfinity(availableSize.Width) == true ? itemWidth * this._ColumnCount : availableSize.Width,
                double.IsInfinity(availableSize.Height) == true ? extent.Height : availableSize.Height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var itemWidth = this._EffectiveItemWidth;
            var itemHeight = this.ResolvedItemHeight;

            var generator = this.ItemContainerGenerator;
            var children = this.InternalChildren;

            for (int childIndex = 0; childIndex < children.Count; childIndex++)
            {
                var index = generator.IndexFromGeneratorPosition(new(childIndex, 0));
                if (index < 0)
                {
                    continue;
                }

                var column = index % this._ColumnCount;
                var row = index / this._ColumnCount;

                // Arranging at the scrolled position keeps every child inside the panel's own
                // bounds, so the scroll presenter's clip is the only one needed.
                children[childIndex].Arrange(new(
                    column * itemWidth,
                    (row * itemHeight) - this._Offset.Y,
                    itemWidth,
                    itemHeight));
            }

            return finalSize;
        }

        protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
        {
            switch (args.Action)
            {
                case NotifyAction.Remove:
                case NotifyAction.Replace:
                case NotifyAction.Move:
                {
                    this.RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
                    break;
                }

                case NotifyAction.Reset:
                {
                    this.RemoveInternalChildRange(0, this.InternalChildren.Count);

                    // A reset usually means the list was refiltered; showing the middle of a
                    // list the user has not seen yet would be disorienting.
                    this._Offset.Y = 0;
                    this.ScrollOwner?.InvalidateScrollInfo();
                    break;
                }
            }

            base.OnItemsChanged(sender, args);
        }

        private void RealizeVisibleRange(int itemCount, double itemHeight, Size childConstraint)
        {
            if (itemCount == 0)
            {
                this.RemoveInternalChildRange(0, this.InternalChildren.Count);
                return;
            }

            var firstRow = Math.Max(0, (int)Math.Floor(this._Offset.Y / itemHeight));
            var lastRow = Math.Max(
                firstRow,
                (int)Math.Ceiling((this._Offset.Y + Math.Max(itemHeight, this._Viewport.Height)) / itemHeight) - 1);

            var firstIndex = firstRow * this._ColumnCount;
            var lastIndex = Math.Min(itemCount - 1, ((lastRow + 1) * this._ColumnCount) - 1);

            var generator = this.ItemContainerGenerator;
            var startPosition = generator.GeneratorPositionFromIndex(firstIndex);

            // When the first visible item already has a container, generation must start on
            // the one after it.
            var childIndex = startPosition.Offset == 0
                ? startPosition.Index
                : startPosition.Index + 1;

            using (generator.StartAt(startPosition, GeneratorDirection.Forward, true))
            {
                for (int i = firstIndex; i <= lastIndex; i++, childIndex++)
                {
                    var child = (UIElement)generator.GenerateNext(out var isNewlyRealized);
                    if (child == null)
                    {
                        break;
                    }

                    if (isNewlyRealized == true)
                    {
                        if (childIndex >= this.InternalChildren.Count)
                        {
                            this.AddInternalChild(child);
                        }
                        else
                        {
                            this.InsertInternalChild(childIndex, child);
                        }
                        generator.PrepareItemContainer(child);
                    }

                    child.Measure(childConstraint);
                }
            }

            this.CleanUpOutsideRange(firstIndex, lastIndex);
        }

        private void CleanUpOutsideRange(int firstIndex, int lastIndex)
        {
            var generator = this.ItemContainerGenerator;

            for (int i = this.InternalChildren.Count - 1; i >= 0; i--)
            {
                var position = new GeneratorPosition(i, 0);
                var index = generator.IndexFromGeneratorPosition(position);

                if (index >= firstIndex && index <= lastIndex)
                {
                    continue;
                }

                generator.Remove(position, 1);
                this.RemoveInternalChildRange(i, 1);
            }
        }

        private int GetItemCount()
        {
            // Touching InternalChildren is what instantiates the generator; without it the
            // owner reports no items at all.
            var children = this.InternalChildren;
            var owner = ItemsControl.GetItemsOwner(this);
            return owner?.Items.Count ?? children.Count;
        }

        private bool UpdateScrollInfo(Size extent, Size viewport)
        {
            var changed = false;

            if (extent.Equals(this._Extent) == false)
            {
                this._Extent = extent;
                changed = true;
            }

            if (viewport.Equals(this._Viewport) == false)
            {
                this._Viewport = viewport;
                changed = true;
            }

            var maximumOffset = Math.Max(0, this._Extent.Height - this._Viewport.Height);
            if (this._Offset.Y > maximumOffset)
            {
                this._Offset.Y = maximumOffset;
                changed = true;
            }

            return changed;
        }

        private UIElement FindDirectChild(Visual visual)
        {
            DependencyObject current = visual;
            while (current != null && this.InternalChildren.Contains(current as UIElement) == false)
            {
                current = VisualTreeHelper.GetParent(current);
            }
            return current as UIElement;
        }

        private int IndexFromContainer(UIElement child)
        {
            var childIndex = this.InternalChildren.IndexOf(child);
            return childIndex < 0
                ? -1
                : this.ItemContainerGenerator.IndexFromGeneratorPosition(new(childIndex, 0));
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            if (value < minimum)
            {
                return minimum;
            }
            return value > maximum ? maximum : value;
        }
    }
}
