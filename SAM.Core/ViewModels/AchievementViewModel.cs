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
using System.Threading;
using SAM.Core.Caching;
using SAM.Core.Steam.Schema;
using static SAM.Core.InvariantShorthand;

namespace SAM.Core.ViewModels
{
    /// <summary>
    /// One achievement, with the pending state the user is building up before a store.
    /// </summary>
    public sealed class AchievementViewModel : ObservableObject
    {
        private const string _IconHost = "https://cdn.steamstatic.com";

        /// <summary>Below this, a rarity badge is called out as ultra-rare.</summary>
        internal const double _UltraRareThreshold = 5.0;

        private readonly uint _AppId;
        private readonly SynchronizationContext _Context;
        private string _IconNormal;
        private string _IconLocked;

        private bool _IsAchieved;
        private bool _IsUnlocked;
        private DateTime? _UnlockTime;
        private bool _ShowSecretDetails;
        private double? _RarityPercentage;

        public AchievementViewModel(uint appId, AchievementDefinition definition, bool isAchieved, DateTime? unlockTime)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            this._AppId = appId;
            this._Context = SynchronizationContext.Current;
            this.Id = definition.Id;
            this.ApplyDefinition(definition);

            this._IsAchieved = isAchieved;
            this._IsUnlocked = isAchieved;
            this._UnlockTime = unlockTime;
        }

        public string Id { get; }

        public string Name { get; private set; }

        public string Description { get; private set; }

        public bool IsHidden { get; private set; }

        public int Permission { get; private set; }

        /// <summary>
        /// True while this achievement's real name, description and icon should be shown
        /// even though it is still hidden and locked -- set from a hover or the manager's
        /// "reveal hidden achievements" toggle. Has no effect once the achievement either
        /// isn't hidden or has actually been earned; both already show their real details.
        /// </summary>
        public bool ShowSecretDetails
        {
            get => this._ShowSecretDetails;
            set
            {
                if (this.Set(ref this._ShowSecretDetails, value) == false)
                {
                    return;
                }

                this.Raise(nameof(this.DisplayName), nameof(this.DisplayDescription), nameof(this.IconIdentity), nameof(this.IconUri));
            }
        }

        /// <summary>
        /// What is actually shown for the name: the real one, unless this is still a locked,
        /// unrevealed secret, in which case a generic placeholder stands in for it so the
        /// list doesn't spoil what earning it involves.
        /// </summary>
        public string DisplayName => this.IsObscured == false ? this.Name : "Hidden Achievement";

        /// <summary>See <see cref="DisplayName"/>; the same obscuring rule for the description.</summary>
        public string DisplayDescription => this.IsObscured == false
            ? this.Description
            : "This achievement is hidden until unlocked.";

        /// <summary>
        /// The globally-cached fraction of owners who have unlocked this achievement, as a
        /// percentage in [0, 100]. Null until Steam has actually supplied one.
        /// </summary>
        public double? RarityPercentage
        {
            get => this._RarityPercentage;
            internal set
            {
                if (this.Set(ref this._RarityPercentage, value) == true)
                {
                    this.Raise(nameof(this.IsUltraRare), nameof(this.RarityText));
                }
            }
        }

        /// <summary>True once a known rarity is under the ultra-rare threshold.</summary>
        public bool IsUltraRare => this._RarityPercentage.HasValue == true && this._RarityPercentage.Value < _UltraRareThreshold;

        /// <summary>A short "Rare: 1.4%" label for the badge, or null when rarity isn't known yet.</summary>
        public string RarityText => this._RarityPercentage.HasValue == false
            ? null
            : _($"{(this.IsUltraRare == true ? "Rare" : "Unlocked by")}: {this._RarityPercentage.Value:0.0}%");

        /// <summary>
        /// Steam marks some achievements as owner-only. They are shown, but refuse to change.
        /// </summary>
        /// <remarks>
        /// An achievement's Permission packs two separate restriction flags into its low bits,
        /// and either one is Steam's way of saying this is not for a third-party tool to edit,
        /// so the mask is 3 (0b11) to catch both. Compare
        /// <see cref="StatViewModel.IsProtected"/>, which masks a stat's Permission with only
        /// bit 1: a stat's bit 0 is unrelated schema bookkeeping, not a protection flag, and
        /// folding it into the same mask there would lock stats Steam never actually restricts.
        /// </remarks>
        public bool IsProtected => (this.Permission & 3) != 0;

        /// <summary>Raised when a change to a protected achievement was refused.</summary>
        public event Action<AchievementViewModel> ProtectedChangeRejected;

        /// <summary>What Steam last told us. Only a successful store moves this.</summary>
        public bool IsAchieved
        {
            get => this._IsAchieved;
            private set => this.Set(ref this._IsAchieved, value);
        }

        /// <summary>What the user has asked for, which may differ until they store.</summary>
        public bool IsUnlocked
        {
            get => this._IsUnlocked;
            set
            {
                if (this._IsUnlocked == value)
                {
                    return;
                }

                if (this.IsProtected == true)
                {
                    this.ProtectedChangeRejected?.Invoke(this);
                    this.BounceBinding();
                    return;
                }

                this._IsUnlocked = value;
                this.Raise(nameof(this.IsUnlocked), nameof(this.IsModified));
                this.Changed?.Invoke(this);
            }
        }

        public bool IsModified => this._IsUnlocked != this._IsAchieved;

        public DateTime? UnlockTime
        {
            get => this._UnlockTime;
            private set
            {
                if (this.Set(ref this._UnlockTime, value) == true)
                {
                    this.Raise(nameof(this.UnlockTimeText));
                }
            }
        }

        public string UnlockTimeText => this._UnlockTime.HasValue == true
            ? this._UnlockTime.Value.ToString("g")
            : "";

        /// <summary>Cache identity of the icon for the state currently being shown.</summary>
        public string IconIdentity
        {
            get
            {
                var icon = this.CurrentIconName;
                return icon == null ? null : CacheKey.ForAchievementIcon(this._AppId, icon);
            }
        }

        public Uri IconUri
        {
            get
            {
                var icon = this.CurrentIconName;
                if (icon == null)
                {
                    return null;
                }

                var url = _($"{_IconHost}/steamcommunity/public/images/apps/{this._AppId}/{icon}");
                return Uri.TryCreate(url, UriKind.Absolute, out var uri) == true ? uri : null;
            }
        }

        internal event Action<AchievementViewModel> Changed;

        /// <summary>
        /// Sets the pending state without consulting the protection rule, for bulk operations
        /// that must skip protected achievements rather than prompt for each one.
        /// </summary>
        internal bool TrySetUnlocked(bool value)
        {
            if (this.IsProtected == true || this._IsUnlocked == value)
            {
                return false;
            }

            this._IsUnlocked = value;
            this.Raise(nameof(this.IsUnlocked), nameof(this.IsModified));
            return true;
        }

        /// <summary>Accepts the pending state as the stored truth after a successful store.</summary>
        internal void AcceptPending(DateTime? unlockTime)
        {
            this.IsAchieved = this._IsUnlocked;
            this.UnlockTime = unlockTime;
            this.Raise(
                nameof(this.IsModified),
                nameof(this.DisplayName),
                nameof(this.DisplayDescription),
                nameof(this.IconIdentity),
                nameof(this.IconUri));
        }

        /// <summary>
        /// Refreshes this instance from a freshly read definition and value, so it can be
        /// reused across a reload instead of a new instance being constructed for the same
        /// achievement id.
        /// </summary>
        /// <remarks>
        /// Definition-derived display fields always move to the fresh values -- a redelivered
        /// schema can legitimately carry different text (a language change) or a different
        /// permission, and a reused instance must not go on showing what it was first
        /// constructed with. The pending choice is handled differently: an edit still pending
        /// is left untouched unless the achievement has become protected since it was staged,
        /// in which case it is dropped, exactly as attempting that same edit fresh right now
        /// would be refused by <see cref="TrySetUnlocked"/>. An achievement with nothing
        /// pending simply moves to mirror the fresh value, exactly as a newly constructed
        /// instance would have started out.
        /// </remarks>
        internal void RefreshStoredState(AchievementDefinition definition, bool isAchieved, DateTime? unlockTime)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var hadPendingEdit = this.IsModified;

            this.ApplyDefinition(definition);
            this.IsAchieved = isAchieved;
            this.UnlockTime = unlockTime;

            if (hadPendingEdit == false || this.IsProtected == true)
            {
                this._IsUnlocked = isAchieved;
            }

            this.Raise(
                nameof(this.Name),
                nameof(this.Description),
                nameof(this.DisplayName),
                nameof(this.DisplayDescription),
                nameof(this.IsHidden),
                nameof(this.Permission),
                nameof(this.IsProtected),
                nameof(this.IsUnlocked),
                nameof(this.IsModified),
                nameof(this.IconIdentity),
                nameof(this.IconUri));
        }

        /// <summary>
        /// Sets the fields derived from the schema definition. Shared by the constructor and by
        /// a reload's in-place refresh of a reused instance, so a redelivered schema with
        /// different display text, a different permission, or different icon names is not left
        /// stale on an instance that was reused rather than reconstructed.
        /// </summary>
        private void ApplyDefinition(AchievementDefinition definition)
        {
            this.Description = definition.Description ?? "";
            this.IsHidden = definition.IsHidden;
            this.Permission = definition.Permission;

            // A name beginning with '#' is an unlocalised token, not something to show.
            this.Name = string.IsNullOrEmpty(definition.Name) == true ||
                        definition.Name.StartsWith("#", StringComparison.InvariantCulture) == true
                ? definition.Id
                : definition.Name;

            this._IconNormal = string.IsNullOrEmpty(definition.IconNormal) == true ? null : definition.IconNormal;
            this._IconLocked = string.IsNullOrEmpty(definition.IconLocked) == true ? this._IconNormal : definition.IconLocked;
        }

        /// <summary>Discards the pending state, e.g. after a failed store.</summary>
        internal void RevertPending()
        {
            if (this._IsUnlocked == this._IsAchieved)
            {
                return;
            }

            this._IsUnlocked = this._IsAchieved;
            this.Raise(nameof(this.IsUnlocked), nameof(this.IsModified));
        }

        public bool Matches(string search)
        {
            if (string.IsNullOrEmpty(search) == true)
            {
                return true;
            }

            return (this.Name != null && this.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (this.Description != null && this.Description.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// Pushes the real state back at a two-way binding that tried to change it.
        /// </summary>
        /// <remarks>
        /// A notification raised inside the setter arrives while the binding is still writing
        /// and gets discarded, so it has to be posted back to the UI thread instead. Using the
        /// synchronization context rather than a dispatcher keeps this assembly free of a
        /// presentation framework.
        /// </remarks>
        private void BounceBinding()
        {
            var context = this._Context;
            if (context == null)
            {
                this.Raise(nameof(this.IsUnlocked));
                return;
            }

            context.Post(_ => this.Raise(nameof(this.IsUnlocked)), null);
        }

        /// <summary>
        /// True while the real name/description are replaced by a generic placeholder:
        /// still hidden in the schema, not yet earned, and nothing has revealed it.
        /// </summary>
        private bool IsObscured => this.IsHidden == true && this._IsAchieved == false && this._ShowSecretDetails == false;

        private string CurrentIconName
        {
            get
            {
                // A hidden achievement's real icon is only shown once earned or revealed;
                // everything else follows the ordinary achieved/locked art.
                var showRealArt = this._IsAchieved == true || (this.IsHidden == true && this._ShowSecretDetails == true);
                return showRealArt == true ? this._IconNormal : this._IconLocked;
            }
        }

        public override string ToString() => $"{this.Name} ({(this._IsUnlocked ? "unlocked" : "locked")})";
    }
}
