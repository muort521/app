// Copyright (C) 2026 Stratum fork contributors
// SPDX-License-Identifier: GPL-3.0-only

using Stratum.Core.Backup;

namespace Stratum.Droid.Shared.Wear
{
    /// <summary>
    /// Sent from the watch to the phone when a cloud backup is restored
    /// from the watch's cloud backup list.
    /// </summary>
    public class WearRestoreRequest
    {
        public string FileName { get; set; }
        public Backup Backup { get; set; }
    }
}
