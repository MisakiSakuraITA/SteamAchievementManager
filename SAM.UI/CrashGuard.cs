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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SAM.UI
{
    /// <summary>
    /// Turns an unhandled exception into a message the user can act on instead of a process
    /// that vanishes without explanation.
    /// </summary>
    /// <remarks>
    /// Only a dispatcher exception can actually be recovered from; the process-wide and
    /// task-scheduler hooks exist so the failure is at least reported before the runtime
    /// takes over. Both applications install this at startup.
    /// </remarks>
    public static class CrashGuard
    {
        private static readonly object _Lock = new();

        private static bool _IsInstalled;
        private static string _ApplicationName = "Steam Achievement Manager";
        private static bool _IsReporting;

        public static void Install(Application application, string applicationName)
        {
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            lock (_Lock)
            {
                if (_IsInstalled == true)
                {
                    return;
                }
                _IsInstalled = true;
            }

            if (string.IsNullOrEmpty(applicationName) == false)
            {
                _ApplicationName = applicationName;
            }

            application.DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // Recoverable: the dispatcher can keep pumping once the exception is marked
            // handled, so the user keeps their window and any unstored changes in it.
            e.Handled = true;
            Report(e.Exception, isFatal: false);
        }

        private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // Not recoverable; the runtime tears the process down after this returns. The
            // most that can be done is tell the user why.
            Report(e.ExceptionObject as Exception, isFatal: e.IsTerminating);
        }

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            // Observing it keeps the default escalation policy from ever applying.
            e.SetObserved();
        }

        /// <summary>
        /// Reports a fault from a Steam callback subscriber. The pump isolates the fault and
        /// keeps running regardless, so nothing here is actually recoverable from the user's
        /// point of view -- this exists purely so a fault that would otherwise vanish silently
        /// gets the same visibility as any other recovered error.
        /// </summary>
        public static void ReportCallbackFault(Exception exception)
        {
            Report(exception, isFatal: false);
        }

        private static void Report(Exception exception, bool isFatal)
        {
            // A fault raised while a fault is already being reported would otherwise recurse
            // through the dispatcher hook until the stack runs out.
            lock (_Lock)
            {
                if (_IsReporting == true)
                {
                    return;
                }
                _IsReporting = true;
            }

            try
            {
                var detail = exception == null
                    ? "An unknown error occurred."
                    : exception.GetType().Name + ": " + exception.Message;

                var message = isFatal == true
                    ? _ApplicationName + " has to close because of an unexpected error.\n\n" + detail
                    : _ApplicationName + " hit an unexpected error and has recovered.\n\n" + detail +
                      "\n\nIf anything looks wrong, reload before storing changes.";

                ShowMessage(message, isFatal);
            }
            catch (Exception)
            {
                // Reporting a crash must never itself crash.
            }
            finally
            {
                lock (_Lock)
                {
                    _IsReporting = false;
                }
            }
        }

        private static void ShowMessage(string message, bool isFatal)
        {
            var icon = isFatal == true ? MessageBoxImage.Error : MessageBoxImage.Warning;
            var caption = isFatal == true ? "Unexpected error" : "Recovered from an error";

            var application = Application.Current;
            if (application == null)
            {
                MessageBox.Show(message, caption, MessageBoxButton.OK, icon);
                return;
            }

            // The process-wide hook can fire from any thread, and MessageBox has to be shown
            // from the dispatcher.
            if (application.Dispatcher.CheckAccess() == true)
            {
                MessageBox.Show(message, caption, MessageBoxButton.OK, icon);
                return;
            }

            application.Dispatcher.Invoke(
                () => MessageBox.Show(message, caption, MessageBoxButton.OK, icon));
        }
    }
}
