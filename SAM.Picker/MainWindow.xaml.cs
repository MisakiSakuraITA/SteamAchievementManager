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
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SAM.Core.Steam;
using SAM.Core.Threading;
using SAM.Core.ViewModels;
using SAM.UI;
using SAM.UI.Imaging;

namespace SAM.Picker
{
    public partial class MainWindow : ThemedWindow
    {
        private const long _CapsuleCacheBudgetBytes = 80L * 1024 * 1024;
        private const int _MaximumConcurrentCapsuleLoads = 6;

        private static readonly TimeSpan _CacheRetention = TimeSpan.FromDays(90);
        private static readonly TimeSpan _CallbackInterval = TimeSpan.FromMilliseconds(100);

        private readonly GameLibraryViewModel _Library;
        private readonly DispatcherTimer _CallbackTimer;

        public MainWindow(ISteamLibraryService steam)
            : this(new GameLibraryViewModel(
                steam ?? throw new ArgumentNullException(nameof(steam))))
        {
        }

        public MainWindow(GameLibraryViewModel library)
        {
            if (library == null)
            {
                throw new ArgumentNullException(nameof(library));
            }

            this.Capsules = new("logos", _MaximumConcurrentCapsuleLoads, _CapsuleCacheBudgetBytes);

            this._Library = library;
            this._Library.LaunchRequested += this.OnLaunchRequested;
            this._Library.ErrorRaised += this.OnErrorRaised;

            this.InitializeComponent();

            this.DataContext = this._Library;

            this._CallbackTimer = new(DispatcherPriority.Background, this.Dispatcher)
            {
                Interval = _CallbackInterval,
            };
            this._CallbackTimer.Tick += this.OnCallbackTick;
        }

        /// <summary>
        /// Bound by the card template. Exposed on the window rather than the view model so the
        /// view model stays free of anything that decodes an image.
        /// </summary>
        public ImageSourceCache Capsules { get; }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            this.Capsules.SchedulePrune(_CacheRetention);
            this._CallbackTimer.Start();
            this._Library.LoadAsync().Forget();
        }

        protected override void OnClosed(EventArgs e)
        {
            this._CallbackTimer.Stop();
            this._CallbackTimer.Tick -= this.OnCallbackTick;

            this._Library.LaunchRequested -= this.OnLaunchRequested;
            this._Library.ErrorRaised -= this.OnErrorRaised;
            this._Library.Shutdown();

            this.Capsules.Dispose();

            base.OnClosed(e);
        }

        private void OnCallbackTick(object sender, EventArgs e)
        {
            this._Library.RunCallbacks();
        }

        private void OnLaunchRequested(GameViewModel game)
        {
            if (game == null)
            {
                return;
            }

            try
            {
                ProcessStartInfo startInfo = new("SAM.Game.exe", game.Id.ToString(CultureInfo.InvariantCulture))
                {
                    UseShellExecute = false,
                    WorkingDirectory = AppContext.BaseDirectory,
                };
                Process.Start(startInfo);
            }
            catch (Win32Exception)
            {
                this.OnErrorRaised("Failed to start SAM.Game.exe.");
            }
            catch (System.IO.FileNotFoundException)
            {
                this.OnErrorRaised("Failed to start SAM.Game.exe.");
            }
        }

        private void OnErrorRaised(string message)
        {
            MessageBox.Show(this, message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void OnGameActivated(object sender, MouseButtonEventArgs e)
        {
            // Routed through the command, rather than calling OnLaunchRequested directly, so
            // CanExecute stays the one place that decides whether a launch is allowed.
            if (this._GameList.SelectedItem is GameViewModel game &&
                this._Library.LaunchCommand.CanExecute(game) == true)
            {
                this._Library.LaunchCommand.Execute(game);
            }
        }

        private void OnGameListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            if (this._GameList.SelectedItem is GameViewModel game)
            {
                e.Handled = true;

                if (this._Library.LaunchCommand.CanExecute(game) == true)
                {
                    this._Library.LaunchCommand.Execute(game);
                }
            }
        }

        private void OnAddGameKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            this._Library.AddGameCommand.Execute(this._AddGameBox.Text);
            e.Handled = true;
        }
    }
}
