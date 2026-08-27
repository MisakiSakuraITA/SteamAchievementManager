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
using System.Windows;
using SAM.Core.Net;
using SAM.Core.Steam;

namespace SAM.Picker
{
    public partial class App : Application
    {
        private API.Client _SteamClient;
        private SteamLibraryService _SteamLibrary;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            SAM.UI.CrashGuard.Install(this, "Steam Achievement Manager");

            if (IsRunningFromSteamDirectory() == true)
            {
                Fail("This tool declines to being run from the Steam directory.");
                return;
            }

            this._SteamClient = new();
            try
            {
                this._SteamClient.Initialize(0);
            }
            catch (API.ClientInitializeException ex)
            {
                Fail(string.IsNullOrEmpty(ex.Message) == true
                    ? "Steam is not running. Please start Steam then run this tool again."
                    : "Steam is not running. Please start Steam then run this tool again.\n\n(" + ex.Message + ")");
                return;
            }
            catch (DllNotFoundException)
            {
                Fail("You've caused an exceptional error!");
                return;
            }

            this._SteamLibrary = new(this._SteamClient);

            MainWindow window = new(this._SteamLibrary);
            this.MainWindow = window;
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            HttpDownloader.Shutdown();

            // Unhook from the pipe before releasing it, so nothing is left registered to
            // receive an event against a client that is going away.
            this._SteamLibrary?.Dispose();
            this._SteamLibrary = null;

            this._SteamClient?.Dispose();
            this._SteamClient = null;

            base.OnExit(e);
        }

        private static bool IsRunningFromSteamDirectory()
        {
            var installPath = API.Steam.GetInstallPath();
            if (string.IsNullOrEmpty(installPath) == true)
            {
                return false;
            }

            var startupPath = AppContext.BaseDirectory;
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(installPath)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(startupPath)),
                StringComparison.OrdinalIgnoreCase);
        }

        private void Fail(string message)
        {
            MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            this.Shutdown(1);
        }
    }
}
