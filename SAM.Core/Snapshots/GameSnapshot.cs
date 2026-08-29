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
using System.Collections.Generic;

namespace SAM.Core.Snapshots
{
    /// <summary>
    /// A portable backup of one app's achievement and statistic state, as shown at the
    /// moment it was captured -- including anything still only staged, not yet stored to
    /// Steam. See <see cref="GameSnapshotSerializer"/> for reading and writing one as JSON or
    /// CSV, and <c>AchievementManagerViewModel.BuildSnapshot</c> /
    /// <c>AchievementManagerViewModel.TryApplySnapshot</c> for producing and applying one.
    /// </summary>
    public sealed class GameSnapshot
    {
        public uint AppId { get; set; }

        public DateTime Timestamp { get; set; }

        public List<AchievementSnapshotEntry> Achievements { get; set; } = new();

        public List<StatisticSnapshotEntry> Statistics { get; set; } = new();
    }

    /// <summary>One achievement's recorded state within a <see cref="GameSnapshot"/>.</summary>
    public sealed class AchievementSnapshotEntry
    {
        public string Id { get; set; }

        public bool IsAchieved { get; set; }

        /// <summary>
        /// The real unlock time as of the capture, if it has one. Never set by importing a
        /// snapshot back -- an unlock time only ever comes from an actual store.
        /// </summary>
        public DateTime? UnlockTime { get; set; }
    }

    /// <summary>One statistic's recorded value within a <see cref="GameSnapshot"/>.</summary>
    public sealed class StatisticSnapshotEntry
    {
        public string Id { get; set; }

        public double Value { get; set; }
    }
}
