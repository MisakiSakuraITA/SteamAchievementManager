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

namespace SAM.Core.Protocol
{
    /// <summary>
    /// Registers (and unregisters) this application as the handler for its own
    /// <see cref="ProtocolUri.Scheme"/> URI scheme, entirely at the user's own request -- SAM
    /// never registers itself on its own.
    /// </summary>
    public interface IProtocolHandlerService
    {
        /// <summary>Whether the scheme is currently registered to this installation.</summary>
        bool IsRegistered { get; }

        /// <summary>Registers the scheme. Safe to call again if already registered.</summary>
        void Register();

        /// <summary>Removes the registration. Safe to call whether or not one exists.</summary>
        void Unregister();
    }

    /// <summary>
    /// A no-op <see cref="IProtocolHandlerService"/>, so code that has nothing to say about
    /// protocol registration is not forced to depend on the real, registry-backed one.
    /// </summary>
    public sealed class NullProtocolHandlerService : IProtocolHandlerService
    {
        public static readonly NullProtocolHandlerService Instance = new();

        private NullProtocolHandlerService()
        {
        }

        public bool IsRegistered => false;

        public void Register()
        {
        }

        public void Unregister()
        {
        }
    }
}
