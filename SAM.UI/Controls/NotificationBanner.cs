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
using System.Windows.Input;

namespace SAM.UI.Controls
{
    public enum NotificationSeverity
    {
        Error,
        Warning,
        Success,
    }

    /// <summary>
    /// A dismissible, non-modal strip for something that just happened: a failure worth an
    /// optional retry, or a brief confirmation.
    /// </summary>
    /// <remarks>
    /// Deliberately not a <see cref="System.Windows.MessageBox"/>: a transient result should
    /// not stop the user from doing anything else while they decide whether to act on it, and
    /// several can arrive faster than a person can dismiss modal dialogs one at a time. Compare
    /// the shells' own "Steam disconnected" banner, which is for a standing condition that
    /// outlives any one acknowledgement rather than a one-off result.
    /// </remarks>
    public class NotificationBanner : Control
    {
        static NotificationBanner()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(NotificationBanner),
                new FrameworkPropertyMetadata(typeof(NotificationBanner)));
        }

        public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
            nameof(IsOpen),
            typeof(bool),
            typeof(NotificationBanner),
            new FrameworkPropertyMetadata(false));

        public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
            nameof(Message),
            typeof(string),
            typeof(NotificationBanner),
            new PropertyMetadata(""));

        public static readonly DependencyProperty SeverityProperty = DependencyProperty.Register(
            nameof(Severity),
            typeof(NotificationSeverity),
            typeof(NotificationBanner),
            new FrameworkPropertyMetadata(NotificationSeverity.Error));

        public static readonly DependencyProperty RetryCommandProperty = DependencyProperty.Register(
            nameof(RetryCommand),
            typeof(ICommand),
            typeof(NotificationBanner),
            new FrameworkPropertyMetadata(null));

        private Button _RetryButton;
        private Button _DismissButton;

        /// <summary>Whether the banner is currently shown. Set by the host to raise one.</summary>
        public bool IsOpen
        {
            get => (bool)this.GetValue(IsOpenProperty);
            set => this.SetValue(IsOpenProperty, value);
        }

        public string Message
        {
            get => (string)this.GetValue(MessageProperty);
            set => this.SetValue(MessageProperty, value);
        }

        public NotificationSeverity Severity
        {
            get => (NotificationSeverity)this.GetValue(SeverityProperty);
            set => this.SetValue(SeverityProperty, value);
        }

        /// <summary>
        /// What Retry does, or <see langword="null"/> to hide the Retry button entirely --
        /// there is nothing to offer retrying a validation message, for instance.
        /// </summary>
        public ICommand RetryCommand
        {
            get => (ICommand)this.GetValue(RetryCommandProperty);
            set => this.SetValue(RetryCommandProperty, value);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (this._DismissButton != null)
            {
                this._DismissButton.Click -= this.OnDismissClicked;
            }
            if (this._RetryButton != null)
            {
                this._RetryButton.Click -= this.OnRetryClicked;
            }

            this._DismissButton = this.GetTemplateChild("PART_DismissButton") as Button;
            this._RetryButton = this.GetTemplateChild("PART_RetryButton") as Button;

            if (this._DismissButton != null)
            {
                this._DismissButton.Click += this.OnDismissClicked;
            }
            if (this._RetryButton != null)
            {
                this._RetryButton.Click += this.OnRetryClicked;
            }
        }

        private void OnDismissClicked(object sender, RoutedEventArgs e)
        {
            this.IsOpen = false;
        }

        private void OnRetryClicked(object sender, RoutedEventArgs e)
        {
            // Closed first: the retry itself, if it fails too, raises a fresh notification
            // rather than leaving this one's Retry button sitting there while a new attempt
            // is already running.
            this.IsOpen = false;

            var command = this.RetryCommand;
            if (command != null && command.CanExecute(null) == true)
            {
                command.Execute(null);
            }
        }
    }
}
