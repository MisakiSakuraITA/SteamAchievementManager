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
using System.Windows.Media;
using TextOptions = System.Windows.Media.TextOptions;
using SAM.UI.Interop;

namespace SAM.UI
{
    /// <summary>
    /// Base window that paints itself from the theme and darkens its native frame.
    /// </summary>
    public class ThemedWindow : Window
    {
        public ThemedWindow()
        {
            this.Background = Resolve("Brush.Bg.Base", Brushes.Black);
            this.Foreground = Resolve("Brush.Text.Primary", Brushes.White);
            this.FontFamily = ResolveFont("Font.Ui");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            this.UseLayoutRounding = true;
            this.SnapsToDevicePixels = true;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            DarkTitleBar.Apply(
                this,
                ResolveColor("Color.Bg.Surface", Colors.Black),
                ResolveColor("Color.Border.Subtle", Colors.Black),
                ResolveColor("Color.Text.Primary", Colors.White));
        }

        private static Brush Resolve(string key, Brush fallback)
        {
            return Application.Current?.TryFindResource(key) as Brush ?? fallback;
        }

        private static Color ResolveColor(string key, Color fallback)
        {
            var resource = Application.Current?.TryFindResource(key);
            return resource is Color color ? color : fallback;
        }

        private static FontFamily ResolveFont(string key)
        {
            return Application.Current?.TryFindResource(key) as FontFamily ?? new FontFamily("Segoe UI");
        }
    }
}
