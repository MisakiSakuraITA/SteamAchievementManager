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
using System.Windows.Input;
using System.Windows.Threading;
using SAM.Core.Steam;
using SAM.Core.ViewModels;
using SAM.UI;
using SAM.UI.Controls;
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

            // Background is outranked by input, so sustained scrolling of a large achievement
            // list could starve the tick indefinitely -- and with it, the liveness check at
            // the end of RunCallbacks that notices Steam has gone away. Input keeps the pump
            // a peer of input processing instead of subordinate to it.
            this._CallbackTimer = new(DispatcherPriority.Input, this.Dispatcher)
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
            // Every ErrorRaised the manager sends today traces back to (re)loading or
            // storing, so offering to reload is a reasonable default even for the rarer
            // message where it does not undo the specific failure -- a store failure has
            // already reverted the pending edit that failed by the time this fires, so
            // reloading here is harmless rather than a genuine retry of the store itself.
            this.ShowNotification(message, NotificationSeverity.Error, this._Manager.ReloadCommand);
        }

        private void OnInfoRaised(string message)
        {
            this.ShowNotification(message, NotificationSeverity.Success, null);
        }

        private void OnProtectedChangeRejected(AchievementViewModel achievement)
        {
            this.ShowNotification(
                "Sorry, but this is a protected achievement and cannot be managed with Steam Achievement Manager.",
                NotificationSeverity.Warning,
                null);
        }

        private void ShowNotification(string message, NotificationSeverity severity, ICommand retry)
        {
            this._Notification.Message = message;
            this._Notification.Severity = severity;
            this._Notification.RetryCommand = retry;
            this._Notification.IsOpen = true;
        }

        /// <summary>Peeks at a hidden achievement's real details while the pointer is over its card.</summary>
        private void OnAchievementCardMouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: AchievementViewModel achievement })
            {
                achievement.ShowSecretDetails = true;
            }
        }

        /// <summary>
        /// Reverts to whatever the global reveal toggle currently says, rather than always
        /// hiding again -- leaving hover only in charge when the toggle itself is off.
        /// </summary>
        private void OnAchievementCardMouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: AchievementViewModel achievement })
            {
                achievement.ShowSecretDetails = this._Manager.RevealHiddenAchievements;
            }
        }

        /// <summary>
        /// Window-wide shortcuts. Handled on the tunnelling Preview pass so they work no
        /// matter which control currently has focus, rather than only when the element that
        /// would otherwise see the key first happens to be the one that cares about it.
        /// </summary>
        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (this.HandleShortcut(e.Key, Keyboard.Modifiers) == true)
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// The shortcut logic itself, separated from the event handler so it can be exercised
        /// with an explicit modifier set rather than needing Keyboard.Modifiers' real keyboard
        /// state, which nothing outside an actual keypress can fake.
        /// </summary>
        private bool HandleShortcut(Key key, ModifierKeys modifiers)
        {
            switch (key)
            {
                case Key.F when modifiers == ModifierKeys.Control:
                    this._SearchBox.Focus();
                    this._SearchBox.SelectAll();
                    return true;

                case Key.Escape when string.IsNullOrEmpty(this._Manager.SearchText) == false:
                    this._Manager.SearchText = "";
                    return true;

                case Key.F5:
                    if (this._Manager.ReloadCommand.CanExecute(null) == true)
                    {
                        this._Manager.ReloadCommand.Execute(null);
                    }
                    return true;

                case Key.S when modifiers == ModifierKeys.Control:
                    if (this._Manager.StoreCommand.CanExecute(null) == true)
                    {
                        this._Manager.StoreCommand.Execute(null);
                    }
                    return true;

                default:
                    return false;
            }
        }
    }
}
