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
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
        private const int _DecodePixelWidth = 64;

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

        public static readonly DependencyProperty AvatarFilePathProperty = DependencyProperty.Register(
            nameof(AvatarFilePath),
            typeof(string),
            typeof(ActiveAccountBadge),
            new PropertyMetadata(null, OnAvatarFilePathChanged));

        /// <summary>Path to a locally-cached avatar image to render, or null/missing to fall back to the plain user glyph.</summary>
        public string AvatarFilePath
        {
            get => (string)this.GetValue(AvatarFilePathProperty);
            set => this.SetValue(AvatarFilePathProperty, value);
        }

        private static readonly DependencyPropertyKey AvatarImageSourcePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(AvatarImageSource),
            typeof(ImageSource),
            typeof(ActiveAccountBadge),
            new PropertyMetadata(null));

        public static readonly DependencyProperty AvatarImageSourceProperty = AvatarImageSourcePropertyKey.DependencyProperty;

        /// <summary>
        /// The decoded avatar, once loading finishes -- null before that resolves, or
        /// whenever there is nothing to show, so the template's fallback glyph shows through.
        /// </summary>
        public ImageSource AvatarImageSource
        {
            get => (ImageSource)this.GetValue(AvatarImageSourceProperty);
            private set => this.SetValue(AvatarImageSourcePropertyKey, value);
        }

        /// <summary>
        /// Distinguishes the load a path change just started from whichever one was already
        /// in flight, so a stale background decode can never overwrite a newer path's result
        /// after the fact.
        /// </summary>
        private int _LoadToken;

        private static void OnAvatarFilePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((ActiveAccountBadge)d).BeginLoadAvatar((string)e.NewValue);
        }

        private void BeginLoadAvatar(string path)
        {
            var token = ++this._LoadToken;
            this.AvatarImageSource = null;

            if (string.IsNullOrEmpty(path) == true)
            {
                return;
            }

            var dispatcher = this.Dispatcher;
            Task.Run(() =>
            {
                var decoded = TryDecode(path);
                if (decoded == null)
                {
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    // A newer path may have started loading (or cleared this one) while the
                    // decode above was in flight; only the most recent request may still win.
                    if (token == this._LoadToken)
                    {
                        this.AvatarImageSource = decoded;
                    }
                }));
            });
        }

        /// <summary>
        /// Decodes a small, frozen (so it can cross back to the UI thread) copy of the image
        /// at <paramref name="path"/>, off whatever thread calls it. Never throws: a missing
        /// file, an unreadable one, or anything the imaging pipeline rejects all simply mean
        /// there is no avatar to show.
        /// </summary>
        private static BitmapImage TryDecode(string path)
        {
            try
            {
                if (File.Exists(path) == false)
                {
                    return null;
                }

                var bytes = File.ReadAllBytes(path);
                using MemoryStream stream = new(bytes);

                BitmapImage image = new();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = _DecodePixelWidth;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
