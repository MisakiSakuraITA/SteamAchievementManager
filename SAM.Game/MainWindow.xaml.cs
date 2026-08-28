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
using System.Windows.Threading;
using SAM.Core.Steam;
using SAM.Core.ViewModels;
using SAM.UI;
using SAM.UI.Imaging;

namespace SAM.Game
{
    public partial class MainWindow : ThemedWindow
    {
        private const long _IconCacheBudgetBytes = 80L * 1024 * 1024;
        private const int _MaximumConcurrentIconLoads = 6;

        private static readonly TimeSpan _CacheRetention = TimeSpan.FromDays(90);
        private static readonly TimeSpan _CallbackInterval = TimeSpan.FromMilliseconds(100);

        private readonly AchievementManagerViewModel _Manager;
        private readonly DispatcherTimer _CallbackTimer;

        public MainWindow(ISteamStatsService steam)
            : this(new AchievementManagerViewModel(
                steam ?? throw new ArgumentNullException(nameof(steam)),
                new MessageBoxDialogService()))
        {
        }

        public MainWindow(AchievementManagerViewModel manager)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            this.Icons = new("achievements", _MaximumConcurrentIconLoads, _IconCacheBudgetBytes);

            this._Manager = manager;
            this._Manager.ErrorRaised += this.OnErrorRaised;
            this._Manager.InfoRaised += this.OnInfoRaised;
            this._Manager.ProtectedChangeRejected += this.OnProtectedChangeRejected;

            this.InitializeComponent();

            this.DataContext = this._Manager;
            this.Title = "Steam Achievement Manager | " + this._Manager.GameName;

            this._CallbackTimer = new(DispatcherPriority.Background, this.Dispatcher)
            {
                Interval = _CallbackInterval,
            };
            this._CallbackTimer.Tick += this.OnCallbackTick;
        }

        /// <summary>
        /// Bound by the achievement card template. Lives on the window so the view model has
        /// no dependency on anything that decodes an image.
        /// </summary>
        public ImageSourceCache Icons { get; }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            this.Icons.SchedulePrune(_CacheRetention);
            this._CallbackTimer.Start();
            this._Manager.BeginLoad();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);

            if (e.Cancel == true || this._Manager.IsModified == false)
            {
                return;
            }

            var answer = MessageBox.Show(
                this,
                "You have changes that have not been stored to Steam.\n\nClose anyway?",
                "Unstored changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            e.Cancel = answer == MessageBoxResult.No;
        }

        protected override void OnClosed(EventArgs e)
        {
            this._CallbackTimer.Stop();
            this._CallbackTimer.Tick -= this.OnCallbackTick;

            this._Manager.ErrorRaised -= this.OnErrorRaised;
            this._Manager.InfoRaised -= this.OnInfoRaised;
            this._Manager.ProtectedChangeRejected -= this.OnProtectedChangeRejected;
            this._Manager.Shutdown();

            this.Icons.Dispose();

            base.OnClosed(e);
        }

        private void OnCallbackTick(object sender, EventArgs e)
        {
            this._Manager.RunCallbacks();
        }

        private void OnErrorRaised(string message)
        {
            MessageBox.Show(this, message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void OnInfoRaised(string message)
        {
            MessageBox.Show(this, message, "Information", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnProtectedChangeRejected(AchievementViewModel achievement)
        {
            MessageBox.Show(
                this,
                "Sorry, but this is a protected achievement and cannot be managed with Steam Achievement Manager.",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
