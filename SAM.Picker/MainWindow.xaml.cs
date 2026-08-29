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
using System.Windows.Input;
using System.Windows.Threading;
using SAM.Core.Steam;
using SAM.Core.Threading;
using SAM.Core.ViewModels;
using SAM.UI;
using SAM.UI.Controls;
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

            // Background is outranked by input, so sustained scrolling of a large library
            // could starve the tick indefinitely -- and with it, the liveness check at the
            // end of RunCallbacks that notices Steam has gone away. Input keeps the pump a
            // peer of input processing instead of subordinate to it.
            this._CallbackTimer = new(DispatcherPriority.Input, this.Dispatcher)
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

            if (GameProcessLauncher.TryLaunch(game.Id, out _) == false)
            {
                // Nothing sensible to retry here: relaunching the picker's own refresh would
                // not touch whatever kept SAM.Game.exe from starting.
                this.ShowNotification("Failed to start SAM.Game.exe.", NotificationSeverity.Error, null);
            }
        }

        private void OnErrorRaised(string message)
        {
            // Every ErrorRaised from the library today traces back to (re)loading the
            // catalogue or checking ownership, so offering to try that again is a reasonable
            // default even for the rarer validation-style messages, where it is merely a
            // harmless no-op instead of a genuine fix.
            this.ShowNotification(message, NotificationSeverity.Error, this._Library.RefreshCommand);
        }

        private void ShowNotification(string message, NotificationSeverity severity, ICommand retry)
        {
            this._Notification.Message = message;
            this._Notification.Severity = severity;
            this._Notification.RetryCommand = retry;
            this._Notification.IsOpen = true;
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

                case Key.Escape when string.IsNullOrEmpty(this._Library.SearchText) == false:
                    this._Library.SearchText = "";
                    return true;

                case Key.F5:
                    if (this._Library.RefreshCommand.CanExecute(null) == true)
                    {
                        this._Library.RefreshCommand.Execute(null);
                    }
                    return true;

                case Key.Enter when modifiers == ModifierKeys.Control:
                    if (this._Library.LaunchCommand.CanExecute(null) == true)
                    {
                        this._Library.LaunchCommand.Execute(null);
                    }
                    return true;

                default:
                    return false;
            }
        }
    }
}
