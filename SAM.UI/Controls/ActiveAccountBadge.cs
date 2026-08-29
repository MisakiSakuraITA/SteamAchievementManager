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

using System.Windows;
using System.Windows.Controls;

namespace SAM.UI.Controls
{
    /// <summary>
    /// A small, quiet chip that names the Steam account SAM is currently talking to -- so a
    /// user who signed into a different account than they meant to notices immediately,
    /// rather than after editing the wrong library. Shared by both shells so the two never
    /// drift from each other; its template lives in Controls.xaml.
    /// </summary>
    public class ActiveAccountBadge : Control
    {
        static ActiveAccountBadge()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ActiveAccountBadge),
                new FrameworkPropertyMetadata(typeof(ActiveAccountBadge)));
        }

        public static readonly DependencyProperty DisplayNameProperty = DependencyProperty.Register(
            nameof(DisplayName),
            typeof(string),
            typeof(ActiveAccountBadge),
            new PropertyMetadata(null));

        /// <summary>The persona name, or a formatted SteamID64 when the name is not known.</summary>
        public string DisplayName
        {
            get => (string)this.GetValue(DisplayNameProperty);
            set => this.SetValue(DisplayNameProperty, value);
        }

        public static readonly DependencyProperty SteamIdTextProperty = DependencyProperty.Register(
            nameof(SteamIdText),
            typeof(string),
            typeof(ActiveAccountBadge),
            new PropertyMetadata(null));

        public string SteamIdText
        {
            get => (string)this.GetValue(SteamIdTextProperty);
            set => this.SetValue(SteamIdTextProperty, value);
        }
    }
}
