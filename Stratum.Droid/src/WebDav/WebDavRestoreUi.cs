// Copyright (C) 2026 muort
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Android.Widget;
using Stratum.Droid.Activity;

namespace Stratum.Droid.WebDav
{
    /// <summary>
    /// 「从 WebDAV 恢复」：列出云端备份，选择后直接在 Stratum 内恢复，或写回本地备份目录。
    /// 上游只需要在 AddMenuBottomSheet 里加一行菜单、在 MainActivity 里订阅一次点击。
    /// </summary>
    public static class WebDavRestoreUi
    {
        public static async Task ShowAsync(MainActivity activity)
        {
            if (activity == null || activity.IsFinishing) return;

            var config = new WebDavConfig(activity);
            if (!config.IsConfigured)
            {
                Toast.MakeText(activity, Resource.String.webdavNotConfigured, ToastLength.Short).Show();
                return;
            }

            List<WebDavEntry> entries;
            try
            {
                Toast.MakeText(activity, Resource.String.webdavLoading, ToastLength.Short).Show();
                entries = await WebDavBackupService.ListAsync(activity);
            }
            catch (Exception e)
            {
                Toast.MakeText(activity, activity.GetString(Resource.String.webdavLoadFailed, e.Message),
                    ToastLength.Long).Show();
                return;
            }

            if (entries.Count == 0)
            {
                Toast.MakeText(activity, Resource.String.webdavNoBackups, ToastLength.Short).Show();
                return;
            }

            var sheet = new WebDavRestoreBottomSheet(entries);
            sheet.EntrySelected += delegate(object sender, WebDavRestoreEventArgs args)
            {
                ShowActions(activity, args.Entry);
            };

            sheet.Show(activity.SupportFragmentManager, "webdavRestore");
        }

        private static void ShowActions(MainActivity activity, WebDavEntry entry)
        {
            var sheet = new WebDavRestoreActionBottomSheet(entry);
            sheet.RestoreClicked += async delegate { await RestoreToAppAsync(activity, entry); };
            sheet.DownloadClicked += async delegate { await DownloadToLocalAsync(activity, entry); };
            sheet.Show(activity.SupportFragmentManager, "webdavRestoreActions");
        }

        private static async Task RestoreToAppAsync(MainActivity activity, WebDavEntry entry)
        {
            try
            {
                var data = await WebDavBackupService.DownloadBytesAsync(activity, entry);
                await activity.RestoreFromCloudAsync(data, entry.Name);
            }
            catch (Exception e)
            {
                Toast.MakeText(activity, activity.GetString(Resource.String.webdavRestoreFailed, e.Message),
                    ToastLength.Long).Show();
            }
        }

        private static async Task DownloadToLocalAsync(MainActivity activity, WebDavEntry entry)
        {
            try
            {
                await WebDavBackupService.DownloadToLocalBackupDirAsync(activity, entry);
                Toast.MakeText(activity, Resource.String.webdavDownloaded, ToastLength.Short).Show();
            }
            catch (Exception e)
            {
                Toast.MakeText(activity, activity.GetString(Resource.String.webdavRestoreFailed, e.Message),
                    ToastLength.Long).Show();
            }
        }
    }
}
