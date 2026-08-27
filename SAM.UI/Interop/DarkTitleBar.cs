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
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SAM.UI.Interop
{
    /// <summary>
    /// Darkens the native window frame through DWM.
    /// </summary>
    /// <remarks>
    /// Keeping the system title bar and tinting it costs a handful of calls and leaves Snap
    /// Layouts, the system menu and drag behaviour exactly as Windows implements them --
    /// custom chrome would have to reimplement all three. Every attribute here is optional:
    /// older builds reject the ones they do not know and the window simply keeps a light
    /// frame.
    /// </remarks>
    public static class DarkTitleBar
    {
        private const int _DwmwaUseImmersiveDarkModeBefore20H1 = 19;
        private const int _DwmwaUseImmersiveDarkMode = 20;
        private const int _DwmwaBorderColor = 34;
        private const int _DwmwaCaptionColor = 35;
        private const int _DwmwaTextColor = 36;

        /// <summary>
        /// Applies the dark frame to a window. Safe to call before or after the handle exists;
        /// if it does not exist yet, the work is deferred to <c>SourceInitialized</c>.
        /// </summary>
        public static void Apply(Window window, Color caption, Color border, Color text)
        {
            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                window.SourceInitialized += OnSourceInitialized;
                return;
            }

            Apply(handle, caption, border, text);

            void OnSourceInitialized(object sender, EventArgs e)
            {
                window.SourceInitialized -= OnSourceInitialized;
                Apply((Window)sender, caption, border, text);
            }
        }

        public static void Apply(IntPtr handle, Color caption, Color border, Color text)
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }

            // Windows 10 renamed this attribute between builds; setting the newer one first
            // and falling back covers both.
            if (TrySetAttribute(handle, _DwmwaUseImmersiveDarkMode, 1) == false)
            {
                TrySetAttribute(handle, _DwmwaUseImmersiveDarkModeBefore20H1, 1);
            }

            // Windows 11 only. Tinting the caption to the app's own surface colour is what
            // removes the seam between the frame and the content.
            TrySetAttribute(handle, _DwmwaCaptionColor, ToColorRef(caption));
            TrySetAttribute(handle, _DwmwaBorderColor, ToColorRef(border));
            TrySetAttribute(handle, _DwmwaTextColor, ToColorRef(text));
        }

        private static bool TrySetAttribute(IntPtr handle, int attribute, int value)
        {
            try
            {
                return DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int)) == 0;
            }
            catch (DllNotFoundException)
            {
                // No desktop window manager; nothing to tint.
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }

        private static int ToColorRef(Color color)
        {
            return color.R | (color.G << 8) | (color.B << 16);
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }
}
