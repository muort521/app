// Copyright (C) 2026 muort
// SPDX-License-Identifier: GPL-3.0-only

using System;
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Serilog;
using Stratum.Droid.Activity;

namespace Stratum.Droid.WebDav
{
    /// <summary>
    /// WebDAV 上传结果通知。和上游本地自动备份通知分开，避免云端失败被本地成功覆盖。
    /// </summary>
    public static class WebDavNotifier
    {
        private const string ChannelId = "webdav_backup";
        private const int NotificationId = 3001;

        private static readonly ILogger Log = Serilog.Log.ForContext(typeof(WebDavNotifier));

        public static void NotifySuccess(Context context, string fileName)
        {
            Show(context, true, context?.GetString(Resource.String.webdavBackupSuccessText, fileName));
        }

        public static void NotifyFailure(Context context, string error)
        {
            Show(context, false, context?.GetString(Resource.String.webdavBackupFailureText, Shorten(error)));
        }

        private static string Shorten(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "未知错误";
            }

            var singleLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return singleLine.Length <= 180 ? singleLine : singleLine.Substring(0, 180) + "…";
        }

        private static void Show(Context context, bool success, string detail)
        {
            if (context == null)
            {
                return;
            }

            try
            {
#pragma warning disable CA1416
                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    var channel = new NotificationChannel(ChannelId,
                        context.GetString(Resource.String.webdavNotificationChannel),
                        NotificationImportance.Low);
                    NotificationManagerCompat.From(context).CreateNotificationChannel(channel);
                }
#pragma warning restore CA1416

                var builder = new NotificationCompat.Builder(context, ChannelId)
                    .SetSmallIcon(Resource.Drawable.ic_notification)
                    .SetColor(ContextCompat.GetColor(context, Shared.Resource.Color.colorLightBluePrimary))
                    .SetPriority(NotificationCompat.PriorityLow)
                    .SetContentTitle(context.GetString(success
                        ? Resource.String.webdavBackupSuccessTitle
                        : Resource.String.webdavBackupFailureTitle))
                    .SetContentText(detail)
                    .SetStyle(new NotificationCompat.BigTextStyle().BigText(detail))
                    .SetOnlyAlertOnce(true)
                    .SetAutoCancel(true);

                var intent = new Intent(context, typeof(MainActivity));
                intent.SetFlags(ActivityFlags.ClearTask | ActivityFlags.NewTask);
                var pendingIntent = PendingIntent.GetActivity(context, 0, intent, PendingIntentFlags.Immutable);
                builder.SetContentIntent(pendingIntent);

                NotificationManagerCompat.From(context).Notify(NotificationId, builder.Build());
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to show WebDAV backup notification");
            }
        }
    }
}
