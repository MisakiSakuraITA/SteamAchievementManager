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
using Microsoft.Win32;

namespace SAM.Core.Protocol
{
    /// <summary>
    /// <see cref="IProtocolHandlerService"/> backed by the real Windows registry.
    /// </summary>
    /// <remarks>
    /// Registration lives entirely under <c>HKEY_CURRENT_USER\Software\Classes</c> rather than
    /// <c>HKEY_CLASSES_ROOT</c>, which is the standard per-user way a desktop application
    /// registers a URI scheme without requiring administrator elevation -- the same approach
    /// used by other well-known applications that register their own custom protocol.
    /// </remarks>
    public sealed class ProtocolHandlerService : IProtocolHandlerService
    {
        private const string _KeyPath = "Software\\Classes\\" + ProtocolUri.Scheme;
        private const string _CommandKeyPath = _KeyPath + "\\shell\\open\\command";

        private readonly string _ExecutablePath;

        public ProtocolHandlerService(string executablePath)
        {
            if (string.IsNullOrEmpty(executablePath) == true)
            {
                throw new ArgumentNullException(nameof(executablePath));
            }

            this._ExecutablePath = executablePath;
        }

        public bool IsRegistered
        {
            get
            {
                try
                {
                    using var command = Registry.CurrentUser.OpenSubKey(_CommandKeyPath, false);
                    var value = command?.GetValue(null) as string;
                    return string.IsNullOrEmpty(value) == false &&
                           value.IndexOf(this._ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public void Register()
        {
            using var root = Registry.CurrentUser.CreateSubKey(_KeyPath);
            root.SetValue(null, "URL:SAM Protocol", RegistryValueKind.String);
            root.SetValue("URL Protocol", "", RegistryValueKind.String);

            using var command = root.CreateSubKey("shell\\open\\command");
            command.SetValue(null, "\"" + this._ExecutablePath + "\" \"%1\"", RegistryValueKind.String);
        }

        public void Unregister()
        {
            Registry.CurrentUser.DeleteSubKeyTree(_KeyPath, false);
        }
    }
}
