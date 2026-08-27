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

using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SAM.Core.ViewModels
{
    /// <summary>
    /// Change-notification base for every view model in the application.
    /// </summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Assigns a backing field and raises a change notification, returning whether the
        /// value actually moved.
        /// </summary>
        protected bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value) == true)
            {
                return false;
            }

            field = value;
            this.Raise(propertyName);
            return true;
        }

        protected void Raise([CallerMemberName] string propertyName = null)
        {
            this.PropertyChanged?.Invoke(this, new(propertyName));
        }

        protected void Raise(params string[] propertyNames)
        {
            var handler = this.PropertyChanged;
            if (handler == null)
            {
                return;
            }

            foreach (var propertyName in propertyNames)
            {
                handler(this, new(propertyName));
            }
        }
    }
}
