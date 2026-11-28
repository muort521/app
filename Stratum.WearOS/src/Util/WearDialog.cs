// Copyright (C) 2026 Stratum fork contributors
// SPDX-License-Identifier: GPL-3.0-only

using System;
using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using AlertDialog = AndroidX.AppCompat.App.AlertDialog;

namespace Stratum.WearOS.Util
{
    /// <summary>
    /// Compact dialog helpers for round Wear OS screens. The platform alert
    /// dialog is almost the full 480px width and clips its title/actions on a
    /// round display, so we keep custom dialogs inside a small central square.
    /// </summary>
    public static class WearDialog
    {
        public const int DefaultWidthDp = 170;

        public static int Dp(Context context, float value)
        {
            return (int) (value * context.Resources.DisplayMetrics.Density);
        }

        public static AlertDialog Show(Context context, View content, int widthDp = DefaultWidthDp)
        {
            var dialog = new AlertDialog.Builder(context)
                .SetView(content)
                .Create();

            dialog.Show();

            var window = dialog.Window;
            if (window != null)
            {
                // Keep dialogs visually identical to the cloud-backup page:
                // no floating gray panel, just the same solid dark background.
                window.SetBackgroundDrawable(new Android.Graphics.Drawables.ColorDrawable(Color.Black));
                window.SetDimAmount(0f);
                window.ClearFlags(WindowManagerFlags.DimBehind);
                window.SetLayout(Dp(context, widthDp), ViewGroup.LayoutParams.WrapContent);
                window.SetGravity(GravityFlags.Center);
            }

            return dialog;
        }

        public static LinearLayout CreateContainer(Context context)
        {
            var layout = new LinearLayout(context);
            layout.Orientation = Android.Widget.Orientation.Vertical;
            layout.SetPadding(Dp(context, 10), Dp(context, 8), Dp(context, 10), Dp(context, 6));
            return layout;
        }

        public static TextView CreateTitle(Context context, string text)
        {
            var title = new TextView(context);
            title.Text = text;
            title.SetTextSize(Android.Util.ComplexUnitType.Sp, 12);
            title.SetTypeface(null, TypefaceStyle.Bold);
            title.SetTextColor(Color.White);
            title.SetPadding(0, 0, 0, Dp(context, 4));
            return title;
        }

        public static TextView CreateMessage(Context context, string text)
        {
            var message = new TextView(context);
            message.Text = text;
            message.SetTextSize(Android.Util.ComplexUnitType.Sp, 9);
            message.SetTextColor(Color.LightGray);
            message.SetMaxLines(4);
            message.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
            message.SetPadding(0, 0, 0, Dp(context, 4));
            return message;
        }

        public static Button CreateButton(Context context, string text)
        {
            var button = new Button(context);
            button.Text = text;
            button.SetTextSize(Android.Util.ComplexUnitType.Sp, 10);
            button.SetMinHeight(0);
            button.SetMinimumHeight(0);
            button.SetAllCaps(false);
            button.SetTextColor(Color.White);
            button.BackgroundTintList = ColorStateList.ValueOf(Color.ParseColor("#2A2A2A"));
            button.LayoutParameters = new LinearLayout.LayoutParams(0, Dp(context, 34), 1);
            return button;
        }

        public static LinearLayout CreateButtonRow(Context context, Button left, Button right)
        {
            var row = new LinearLayout(context);
            row.Orientation = Android.Widget.Orientation.Horizontal;
            row.SetPadding(0, Dp(context, 6), 0, 0);
            row.LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            row.AddView(left);
            row.AddView(right);
            return row;
        }
    }
}
