// Copyright (C) 2026 muort
// SPDX-License-Identifier: GPL-3.0-only

using Android.App;
using AndroidX.Work;
using Java.Util.Concurrent;

namespace Stratum.Droid
{
    /// <summary>
    /// 数据变化后尽快排一次备份，而不是只等 15 分钟的周期任务。
    /// 单独使用一个 unique name，避免把周期任务覆盖掉。
    /// </summary>
    public static class AutoBackupScheduler
    {
        private const string ImmediateWorkName = AutoBackupWorker.ImmediateName;
        private const int DebounceSeconds = 3;

        public static void ScheduleIfEnabled()
        {
            var context = Application.Context;
            if (context == null)
            {
                return;
            }

            var preferences = new PreferenceWrapper(context);
            if (!preferences.AutoBackupEnabled || preferences.AutoBackupUri == null)
            {
                return;
            }

            var builder = new OneTimeWorkRequest.Builder(typeof(AutoBackupWorker));
            builder.SetInitialDelay(DebounceSeconds, TimeUnit.Seconds!);
            var request = builder.Build();

            WorkManager.GetInstance(context)
                .EnqueueUniqueWork(ImmediateWorkName, ExistingWorkPolicy.Replace!, request);
        }

        /// <summary>
        /// 旧版本的“立即备份”和周期任务共用了 autobackup 这个 unique name，
        /// 点过立即备份后周期任务会被取消。启动时补回来。
        /// </summary>
        public static void EnsurePeriodicIfEnabled()
        {
            var context = Application.Context;
            if (context == null)
            {
                return;
            }

            var preferences = new PreferenceWrapper(context);
            if (!preferences.AutoBackupEnabled || preferences.AutoBackupUri == null)
            {
                return;
            }

            var request = new PeriodicWorkRequest.Builder(typeof(AutoBackupWorker), 15, TimeUnit.Minutes!)
                .Build();

            WorkManager.GetInstance(context)
                .EnqueueUniquePeriodicWork(AutoBackupWorker.Name, ExistingPeriodicWorkPolicy.Keep!, request);
        }
    }
}
