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
using System.Globalization;
using SAM.Core.Caching;

namespace SAM.Core.ViewModels
{
    /// <summary>
    /// One owned game in the library.
    /// </summary>
    /// <remarks>
    /// The capsule is described here, not held here: the view model carries the cache
    /// identity and the source address, and the presentation layer turns those into whatever
    /// image type it needs. That keeps decoded artwork out of a collection that can run to
    /// several thousand entries.
    /// </remarks>
    public sealed class GameViewModel : ObservableObject
    {
        private string _Name;
        private string _CapsuleUrl;
        private Uri _CapsuleUri;

        public GameViewModel(uint id, string type, string name)
        {
            this.Id = id;
            this.Type = type ?? "normal";
            this._Name = Normalize(name, id);
        }

        public uint Id { get; }

        public string Type { get; }

        public string Name
        {
            get => this._Name;
            private set => this.Set(ref this._Name, value);
        }

        /// <summary>The app id as text, for display next to the title.</summary>
        public string IdText => this.Id.ToString(CultureInfo.InvariantCulture);

        /// <summary>Stable cache key for the capsule, or null while the URL is unknown.</summary>
        public string CapsuleIdentity { get; private set; }

        public Uri CapsuleUri
        {
            get => this._CapsuleUri;
            private set => this.Set(ref this._CapsuleUri, value);
        }

        public bool HasCapsule => this._CapsuleUri != null;

        public void UpdateName(string name)
        {
            this.Name = Normalize(name, this.Id);
        }

        /// <summary>
        /// Records the capsule address Steam resolved for this game. Returns whether anything
        /// changed, so callers can avoid redundant reloads.
        /// </summary>
        public bool UpdateCapsule(string capsuleUrl)
        {
            if (string.Equals(this._CapsuleUrl, capsuleUrl, StringComparison.Ordinal) == true)
            {
                return false;
            }

            this._CapsuleUrl = capsuleUrl;

            if (string.IsNullOrEmpty(capsuleUrl) == true ||
                Uri.TryCreate(capsuleUrl, UriKind.Absolute, out var uri) == false)
            {
                this.CapsuleIdentity = null;
                this.CapsuleUri = null;
            }
            else
            {
                // The asset address is part of the identity, so a capsule replaced on the
                // store lands on a different cache entry and invalidates itself.
                this.CapsuleIdentity = CacheKey.ForGameLogo(this.Id, capsuleUrl);
                this.CapsuleUri = uri;
            }

            this.Raise(nameof(this.HasCapsule));
            return true;
        }

        /// <summary>Whether this game matches a free-text search.</summary>
        public bool Matches(string search)
        {
            if (string.IsNullOrEmpty(search) == true)
            {
                return true;
            }

            if (this.Name != null &&
                this.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            // Searching by app id is how you find something Steam has not named yet.
            return this.IdText.StartsWith(search, StringComparison.Ordinal);
        }

        private static string Normalize(string name, uint id)
        {
            return string.IsNullOrEmpty(name) == true
                ? "App " + id.ToString(CultureInfo.InvariantCulture)
                : name;
        }

        public override string ToString() => $"{this.Name} ({this.Id})";
    }
}
