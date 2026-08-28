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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using SAM.Core.Net;
using SAM.Core.Steam;

namespace SAM.Game
{
    public partial class App : Application
    {
        private API.Client _SteamClient;
        private SteamStatsService _SteamStats;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            SAM.UI.CrashGuard.Install(this, "Steam Achievement Manager");

            if (e.Args.Length == 0)
            {
                // Launched on its own: hand over to the picker rather than showing an empty
                // manager, which is what the shortcut in the release archive relies on.
                this.StartPicker();
                this.Shutdown();
                return;
            }

            if (uint.TryParse(e.Args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var appId) == false)
            {
                this.Fail("Could not parse application ID from command line argument.");
                return;
            }

            if (IsRunningFromSteamDirectory() == true)
            {
                this.Fail("This tool declines to being run from the Steam directory.");
                return;
            }

            this._SteamClient = new();
            this._SteamClient.CallbackFaulted += OnCallbackFaulted;
            try
            {
                this._SteamClient.Initialize(appId);
            }
            catch (API.ClientInitializeException ex)
            {
                this.Fail(DescribeInitializeFailure(ex));
                return;
            }
            catch (DllNotFoundException)
            {
                this.Fail("You've caused an exceptional error!");
                return;
            }

            this._SteamStats = new(this._SteamClient, appId);

            MainWindow window = new(this._SteamStats);
            this.MainWindow = window;
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            HttpDownloader.Shutdown();

            // Unhook from the pipe before releasing it, so nothing is left registered to
            // receive an event against a client that is going away.
            this._SteamStats?.Dispose();
            this._SteamStats = null;

            if (this._SteamClient != null)
            {
                this._SteamClient.CallbackFaulted -= OnCallbackFaulted;
            }
            this._SteamClient?.Dispose();
            this._SteamClient = null;

            base.OnExit(e);
        }

        /// <summary>
        /// A callback subscriber threw. The pump isolated it and kept running, so this is
        /// purely a report -- without it, the fault would otherwise vanish with no trace.
        /// </summary>
        private static void OnCallbackFaulted(Exception exception)
        {
            SAM.UI.CrashGuard.ReportCallbackFault(exception);
        }

        private static string DescribeInitializeFailure(API.ClientInitializeException exception)
        {
            if (exception.Failure == API.ClientInitializeFailure.ConnectToGlobalUser)
            {
                return "Steam is not running. Please start Steam then run this tool again.\n\n" +
                       "If you have the game through Family Share, the game may be locked due to\n" +
                       "the Family Share account actively playing a game.\n\n" +
                       "(" + exception.Message + ")";
            }

            return string.IsNullOrEmpty(exception.Message) == true
                ? "Steam is not running. Please start Steam then run this tool again."
                : "Steam is not running. Please start Steam then run this tool again.\n\n(" + exception.Message + ")";
        }

        private static bool IsRunningFromSteamDirectory()
        {
            var installPath = API.Steam.GetInstallPath();
            if (string.IsNullOrEmpty(installPath) == true)
            {
                return false;
            }

            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(installPath)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)),
                StringComparison.OrdinalIgnoreCase);
        }

        private void StartPicker()
        {
            try
            {
                ProcessStartInfo startInfo = new("SAM.Picker.exe")
                {
                    UseShellExecute = false,
                    WorkingDirectory = AppContext.BaseDirectory,
                };
                Process.Start(startInfo);
            }
            catch (Exception)
            {
                MessageBox.Show(
                    "Failed to start SAM.Picker.exe.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void Fail(string message)
        {
            MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            this.Shutdown(1);
        }
    }
}
