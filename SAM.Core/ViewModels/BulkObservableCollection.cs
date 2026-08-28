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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SAM.Core.ViewModels
{
    /// <summary>
    /// An <see cref="ObservableCollection{T}"/> with a bulk replace that raises one change
    /// notification instead of one per item.
    /// </summary>
    /// <remarks>
    /// Rebuilding a filtered view the ordinary way -- <c>Clear()</c> then an <c>Add</c> per
    /// surviving item -- fires a <see cref="INotifyCollectionChanged"/> event for every single
    /// one of those calls. Against a library of a few thousand games that is a few thousand
    /// events per keystroke, each one walking every subscriber (bound containers, the
    /// virtualising panel, anything else listening) for no benefit: nothing downstream needs to
    /// see the intermediate states, only the final one. <see cref="ReplaceAll"/> mutates the
    /// backing list directly, bypassing the overrides that raise those per-item events, and
    /// raises a single <see cref="NotifyCollectionChangedAction.Reset"/> once the swap is done.
    /// </remarks>
    public class BulkObservableCollection<T> : ObservableCollection<T>
    {
        private static readonly PropertyChangedEventArgs _CountChangedEventArgs = new(nameof(Count));
        private static readonly PropertyChangedEventArgs _IndexerChangedEventArgs = new("Item[]");
        private static readonly NotifyCollectionChangedEventArgs _ResetEventArgs = new(NotifyCollectionChangedAction.Reset);

        /// <summary>
        /// Replaces every item in the collection, raising a single <c>Reset</c> notification
        /// once the replacement is complete rather than one notification per item removed and
        /// added.
        /// </summary>
        public void ReplaceAll(IEnumerable<T> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            this.Items.Clear();
            foreach (var item in items)
            {
                this.Items.Add(item);
            }

            this.OnPropertyChanged(_CountChangedEventArgs);
            this.OnPropertyChanged(_IndexerChangedEventArgs);
            this.OnCollectionChanged(_ResetEventArgs);
        }
    }
}
