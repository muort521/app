// Copyright (C) 2026 muort
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using Android.OS;
using Android.Views;
using AndroidX.RecyclerView.Widget;
using Google.Android.Material.TextView;
using Stratum.Droid.Interface;
using Stratum.Droid.Interface.Fragment;
using Stratum.Droid.Interface.ViewHolder;

namespace Stratum.Droid.WebDav
{
    public sealed class WebDavRestoreEventArgs : EventArgs
    {
        public WebDavEntry Entry { get; }

        public WebDavRestoreEventArgs(WebDavEntry entry)
        {
            Entry = entry;
        }
    }

    /// <summary>
    /// 云端备份列表，使用应用统一的底部呼出菜单，而不是对话框。
    /// </summary>
    public sealed class WebDavRestoreBottomSheet : BottomSheet
    {
        private readonly List<WebDavEntry> _entries;

        public WebDavRestoreBottomSheet(List<WebDavEntry> entries)
            : base(Resource.Layout.sheetWebDavRestore, Resource.String.webdavRestoreTitle)
        {
            _entries = entries ?? new List<WebDavEntry>();
        }

        public event EventHandler<WebDavRestoreEventArgs> EntrySelected;

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            var view = base.OnCreateView(inflater, container, savedInstanceState);

            var subtitle = view.FindViewById<MaterialTextView>(Resource.Id.textSubtitle);
            if (subtitle != null)
            {
                subtitle.Visibility = ViewStates.Gone;
            }

            var list = view.FindViewById<RecyclerView>(Resource.Id.listWebDav);
            var adapter = new WebDavRestoreAdapter(_entries);
            adapter.EntryClicked += entry =>
            {
                Dismiss();
                EntrySelected?.Invoke(this, new WebDavRestoreEventArgs(entry));
            };

            list.SetAdapter(adapter);
            list.HasFixedSize = true;
            list.SetLayoutManager(new LinearLayoutManager(Context));

            return view;
        }

        private sealed class WebDavRestoreAdapter : RecyclerView.Adapter
        {
            private readonly List<WebDavEntry> _entries;

            public WebDavRestoreAdapter(List<WebDavEntry> entries)
            {
                _entries = entries;
            }

            public event Action<WebDavEntry> EntryClicked;

            public override int ItemCount => _entries.Count;

            public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
            {
                var itemView = LayoutInflater.From(parent.Context)
                    .Inflate(Resource.Layout.listItemMenu, parent, false);
                var holder = new SheetMenuItemViewHolder(itemView);

                holder.ItemView.Click += delegate
                {
                    var entry = _entries.ElementAtOrDefault(holder.BindingAdapterPosition);
                    if (entry != null)
                    {
                        EntryClicked?.Invoke(entry);
                    }
                };

                return holder;
            }

            public override void OnBindViewHolder(RecyclerView.ViewHolder viewHolder, int position)
            {
                var entry = _entries.ElementAtOrDefault(position);
                if (entry == null)
                {
                    return;
                }

                var holder = (SheetMenuItemViewHolder) viewHolder;
                holder.Icon.SetImageResource(Resource.Drawable.baseline_cloud_24);
                holder.Title.Text = entry.Name;

                var date = entry.LastModified?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "";
                var size = entry.DisplaySize;
                var description = string.IsNullOrEmpty(size) ? date : $"{date}  ·  {size}";

                holder.Description.Visibility = ViewStates.Visible;
                holder.Description.Text = description;
            }
        }
    }

    /// <summary>
    /// 选中某个云端备份后的操作菜单，同样是底部呼出菜单。
    /// </summary>
    public sealed class WebDavRestoreActionBottomSheet : BottomSheet
    {
        private readonly WebDavEntry _entry;

        public WebDavRestoreActionBottomSheet(WebDavEntry entry)
            : base(Resource.Layout.sheetWebDavRestore, Resource.String.webdavRestoreActions)
        {
            _entry = entry;
        }

        public event EventHandler RestoreClicked;
        public event EventHandler DownloadClicked;

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            var view = base.OnCreateView(inflater, container, savedInstanceState);

            var subtitle = view.FindViewById<MaterialTextView>(Resource.Id.textSubtitle);
            if (subtitle != null && _entry != null)
            {
                subtitle.Visibility = ViewStates.Visible;
                subtitle.Text = _entry.Name;
            }

            var list = view.FindViewById<RecyclerView>(Resource.Id.listWebDav);
            SetupMenu(list, new List<SheetMenuItem>
            {
                new SheetMenuItem(Resource.Drawable.baseline_restore_24, Resource.String.webdavRestoreToApp,
                    RestoreClicked),
                new SheetMenuItem(Resource.Drawable.baseline_save_24, Resource.String.webdavDownloadToLocal,
                    DownloadClicked)
            });

            return view;
        }
    }
}
