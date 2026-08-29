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

namespace SAM.Picker
{
    /// <summary>
    /// Starts SAM.Game.exe for a given app id. Shared by the ordinary "manage achievements"
    /// action and by a <c>sam://</c> protocol launch, so both go through exactly one place
    /// that knows how SAM.Game.exe is actually started.
    /// </summary>
    internal static class GameProcessLauncher
    {
        public static bool TryLaunch(uint appId, out Exception failure)
        {
            try
            {
                ProcessStartInfo startInfo = new("SAM.Game.exe", appId.ToString(CultureInfo.InvariantCulture))
                {
                    UseShellExecute = false,
                    WorkingDirectory = AppContext.BaseDirectory,
                };
                Process.Start(startInfo);
                failure = null;
                return true;
            }
            catch (Win32Exception e)
            {
                failure = e;
                return false;
            }
            catch (System.IO.FileNotFoundException e)
            {
                failure = e;
                return false;
            }
        }
    }
}
