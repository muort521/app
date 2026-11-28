// Copyright (C) 2026 Stratum fork contributors
// SPDX-License-Identifier: GPL-3.0-only

namespace Stratum.WearOS.WebDav
{
    /// <summary>
    /// Flags a cache change made by a watch-side restore so MainActivity can
    /// reload its caches instead of immediately overwriting them with the old
    /// phone sync bundle.
    /// </summary>
    public static class WearRestoreState
    {
        public static bool LocalRestorePending { get; set; }
    }
}
