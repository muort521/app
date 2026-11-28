// Copyright (C) 2026 Stratum fork contributors
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.RecyclerView.Widget;
using AndroidX.Wear.Widget;
using Stratum.Droid.Shared.Wear;
using Stratum.WearOS.Cache;
using Stratum.WearOS.Util;
using Stratum.WearOS.WebDav;
using AlertDialog = AndroidX.AppCompat.App.AlertDialog;

namespace Stratum.WearOS.Activity
{
    [Activity(Label = "@string/webdavListTitle", Exported = false)]
    public class WebDavListActivity : AsyncActivity
    {
        private WearWebDavConfig _config;
        private WearableRecyclerView _list;
        private TextView _textEmpty;
        private Button _buttonRefresh;
        private Button _buttonBack;
        private WebDavListAdapter _adapter;
        private AuthenticatorCache _authCache;
        private CategoryCache _categoryCache;
        private CustomIconCache _iconCache;
        private bool _isBusy;

        protected override async Task OnCreateAsync()
        {
            SetTheme(Resource.Style.AppTheme);
            SetContentView(Resource.Layout.activityWebDavList);

            _config = new WearWebDavConfig(this);
            _authCache = Dependencies.Resolve<AuthenticatorCache>();
            _categoryCache = Dependencies.Resolve<CategoryCache>();
            _iconCache = Dependencies.Resolve<CustomIconCache>();
            _list = FindViewById<WearableRecyclerView>(Resource.Id.listWebDav);
            _textEmpty = FindViewById<TextView>(Resource.Id.textEmpty);
            _buttonRefresh = FindViewById<Button>(Resource.Id.buttonRefresh);
            _buttonBack = FindViewById<Button>(Resource.Id.buttonBack);

            await Task.WhenAll(_authCache.InitAsync(), _categoryCache.InitAsync(), _iconCache.InitAsync());

            // Do not add a swipe-to-dismiss callback here: the backup list is vertically
            // scrollable and would dismiss itself while the user scrolls.

            _adapter = new WebDavListAdapter(this);
            _adapter.DeleteClicked += ConfirmDelete;
            _adapter.RestoreClicked += PromptRestore;
            _list.SetAdapter(_adapter);
            _list.HasFixedSize = true;
            _list.SetLayoutManager(new WearableLinearLayoutManager(this));

            _buttonRefresh.Click += async delegate { await LoadAsync(); };
            _buttonBack.Click += delegate { Finish(); };

            await LoadAsync();
        }

        protected override Task OnResumeAsync()
        {
            return Task.CompletedTask;
        }

        private async Task LoadAsync()
        {
            if (_isBusy) return;

            if (!_config.IsConfigured)
            {
                ShowToast(GetString(Resource.String.webdavNotConfigured));
                Finish();
                return;
            }

            SetBusy(true);
            ShowEmpty(GetString(Resource.String.webdavLoading));

            try
            {
                var entries = await WearWebDavService.ListAsync(this);
                _adapter.Replace(entries);
                _list.Visibility = entries.Count == 0 ? ViewStates.Gone : ViewStates.Visible;
                ShowEmpty(entries.Count == 0 ? GetString(Resource.String.webdavListEmpty) : null);
            }
            catch (Exception e)
            {
                ShowToast(GetString(Resource.String.webdavListFailed, ErrorMessage(e)));
                ShowEmpty(GetString(Resource.String.webdavListEmpty));
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void PromptRestore(WebDavEntry entry)
        {
            if (entry == null || _isBusy) return;

            if (!entry.Name.EndsWith(".stratum", StringComparison.OrdinalIgnoreCase))
            {
                ShowToast(GetString(Resource.String.webdavRestoreUnsupported));
                return;
            }

            var container = WearDialog.CreateContainer(this);
            container.AddView(WearDialog.CreateTitle(this, GetString(Resource.String.webdavRestoreTitle)));
            container.AddView(WearDialog.CreateMessage(this,
                GetString(Resource.String.webdavRestoreMessage)));

            var input = new EditText(this);
            input.Hint = GetString(Resource.String.webdavRestorePasswordHint);
            input.InputType = InputTypes.ClassText | InputTypes.TextVariationPassword;
            input.SetSingleLine(true);
            input.SetTextColor(Android.Graphics.Color.White);
            input.LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, WearDialog.Dp(this, 40));
            container.AddView(input);

            var cancel = WearDialog.CreateButton(this, GetString(Resource.String.webdavCancel));
            var confirm = WearDialog.CreateButton(this, GetString(Resource.String.webdavRestore));
            container.AddView(WearDialog.CreateButtonRow(this, cancel, confirm));

            var dialog = WearDialog.Show(this, container);

            cancel.Click += delegate { dialog.Dismiss(); };
            confirm.Click += delegate
            {
                var password = input.Text;
                if (string.IsNullOrEmpty(password))
                {
                    ShowToast(GetString(Resource.String.webdavRestorePasswordEmpty));
                    return;
                }

                dialog.Dismiss();
                _ = RestoreAsync(entry, password);
            };
        }

        private async Task RestoreAsync(WebDavEntry entry, string password)
        {
            if (entry == null || _isBusy) return;

            SetBusy(true);
            ShowEmpty(GetString(Resource.String.webdavRestoreDownloading));

            try
            {
                var data = await WearWebDavService.DownloadAsync(this, entry);
                var backup = await WearBackupFactory.DecryptBackupAsync(data, password);
                await WearBackupFactory.ApplyRestoreAsync(backup, _authCache, _categoryCache, _iconCache);

                WearRestoreState.LocalRestorePending = true;

                var phoneSent = false;
                try
                {
                    await WearPhoneRestoreService.SendAsync(this, new WearRestoreRequest
                    {
                        FileName = entry.Name,
                        Backup = backup
                    });
                    phoneSent = true;
                }
                catch (Exception e)
                {
                    // The watch cache is still restored below. The phone will pick
                    // up the restored data on its next sync if it is reachable.
                    Android.Util.Log.Error("STRATUMWEAR", "Phone restore send failed: " + e);
                }

                var status = GetString(phoneSent
                    ? Resource.String.webdavRestoreSuccess
                    : Resource.String.webdavRestoreLocalOnly, entry.Name);

                _textEmpty.Text = status;
                _textEmpty.Visibility = ViewStates.Visible;
                ShowToast(status);
                await Task.Delay(900);
                Finish();
            }
            catch (Exception e)
            {
                ShowToast(GetString(Resource.String.webdavRestoreFailed, ErrorMessage(e)));
                ShowEmpty(GetString(Resource.String.webdavRestoreFailed, ErrorMessage(e)));
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ConfirmDelete(WebDavEntry entry)
        {
            if (entry == null || _isBusy) return;

            var density = Resources.DisplayMetrics.Density;
            int Dp(int value) => (int) (value * density);

            var layout = new LinearLayout(this);
            layout.Orientation = Orientation.Vertical;
            layout.SetPadding(Dp(12), Dp(8), Dp(12), Dp(4));

            var title = new TextView(this);
            title.Text = GetString(Resource.String.webdavDeleteTitle);
            title.SetTextSize(Android.Util.ComplexUnitType.Sp, 14);
            title.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
            title.SetTextColor(Android.Graphics.Color.White);
            layout.AddView(title);

            var message = new TextView(this);
            message.Text = GetString(Resource.String.webdavDeleteMessage, entry.Name);
            message.SetTextSize(Android.Util.ComplexUnitType.Sp, 10);
            message.SetTextColor(Android.Graphics.Color.LightGray);
            message.SetPadding(0, Dp(4), 0, 0);
            message.SetMaxLines(4);
            message.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
            layout.AddView(message);

            var actions = new LinearLayout(this);
            actions.Orientation = Orientation.Horizontal;
            actions.SetPadding(0, Dp(6), 0, 0);
            actions.LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);

            var cancel = new Button(this);
            cancel.Text = GetString(Resource.String.webdavCancel);
            cancel.SetTextSize(Android.Util.ComplexUnitType.Sp, 10);
            cancel.SetMinHeight(0);
            cancel.SetMinimumHeight(0);
            cancel.SetTextColor(Android.Graphics.Color.White);
            cancel.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(
                Android.Graphics.Color.ParseColor("#2A2A2A"));
            cancel.LayoutParameters = new LinearLayout.LayoutParams(0, WearDialog.Dp(this, 34), 1);

            var delete = new Button(this);
            delete.Text = GetString(Resource.String.webdavDelete);
            delete.SetTextSize(Android.Util.ComplexUnitType.Sp, 10);
            delete.SetMinHeight(0);
            delete.SetMinimumHeight(0);
            delete.SetTextColor(Android.Graphics.Color.ParseColor("#FF8A80"));
            delete.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(
                Android.Graphics.Color.ParseColor("#2A2A2A"));
            delete.LayoutParameters = new LinearLayout.LayoutParams(0, WearDialog.Dp(this, 34), 1);

            actions.AddView(cancel);
            actions.AddView(delete);
            layout.AddView(actions);

            var dialog = WearDialog.Show(this, layout);

            cancel.Click += delegate { dialog.Dismiss(); };
            delete.Click += delegate
            {
                dialog.Dismiss();
                _ = DeleteAsync(entry);
            };
        }

        private async Task DeleteAsync(WebDavEntry entry)
        {
            if (_isBusy) return;

            SetBusy(true);
            try
            {
                await WearWebDavService.DeleteAsync(this, entry);
                _adapter.Remove(entry);

                var count = _adapter.ItemCount;
                _list.Visibility = count == 0 ? ViewStates.Gone : ViewStates.Visible;
                ShowEmpty(count == 0 ? GetString(Resource.String.webdavListEmpty) : null);
            }
            catch (Exception e)
            {
                ShowToast(GetString(Resource.String.webdavDeleteFailed, ErrorMessage(e)));
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ShowEmpty(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                _textEmpty.Visibility = ViewStates.Gone;
                return;
            }

            _textEmpty.Text = text;
            _textEmpty.Visibility = ViewStates.Visible;
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            _buttonRefresh.Enabled = !busy;
            _buttonBack.Enabled = !busy;
        }

        private void ShowToast(string message)
        {
            Toast.MakeText(this, message, ToastLength.Long).Show();
        }

        private static string ErrorMessage(Exception e)
        {
            return string.IsNullOrWhiteSpace(e.Message) ? e.GetType().Name : e.Message;
        }

        private sealed class WebDavListAdapter : RecyclerView.Adapter
        {
            private readonly Context _context;
            private List<WebDavEntry> _entries = new();

            public WebDavListAdapter(Context context)
            {
                _context = context;
            }

            public event Action<WebDavEntry> DeleteClicked;
            public event Action<WebDavEntry> RestoreClicked;

            public override int ItemCount => _entries.Count;

            public void Replace(List<WebDavEntry> entries)
            {
                _entries = entries ?? new List<WebDavEntry>();
                NotifyDataSetChanged();
            }

            public void Remove(WebDavEntry entry)
            {
                var index = _entries.IndexOf(entry);
                if (index < 0) return;

                _entries.RemoveAt(index);
                NotifyItemRemoved(index);
            }

            public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
            {
                var view = LayoutInflater.FromContext(parent.Context)
                    .Inflate(Resource.Layout.webDavListItem, parent, false);
                var holder = new WebDavListHolder(view);

                holder.ItemView.Click += delegate
                {
                    var entry = _entries.ElementAtOrDefault(holder.BindingAdapterPosition);
                    if (entry != null)
                    {
                        RestoreClicked?.Invoke(entry);
                    }
                };

                holder.Delete.Click += delegate
                {
                    var entry = _entries.ElementAtOrDefault(holder.BindingAdapterPosition);
                    if (entry != null)
                    {
                        DeleteClicked?.Invoke(entry);
                    }
                };

                return holder;
            }

            public override void OnBindViewHolder(RecyclerView.ViewHolder viewHolder, int position)
            {
                var entry = _entries.ElementAtOrDefault(position);
                if (entry == null) return;

                var holder = (WebDavListHolder) viewHolder;
                holder.Name.Text = entry.Name;

                var date = entry.LastModified?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "";
                var size = entry.DisplaySize;
                holder.Meta.Text = string.IsNullOrEmpty(size) ? date : $"{date}  ·  {size}";
                holder.RestoreHint.Visibility = entry.Name.EndsWith(".stratum", StringComparison.OrdinalIgnoreCase)
                    ? ViewStates.Visible
                    : ViewStates.Gone;
                holder.ItemView.ContentDescription = entry.Name.EndsWith(".stratum", StringComparison.OrdinalIgnoreCase)
                    ? holder.ItemView.Context.GetString(Resource.String.webdavRestoreContentDescription)
                    : null;
            }

            private sealed class WebDavListHolder : RecyclerView.ViewHolder
            {
                public WebDavListHolder(View itemView) : base(itemView)
                {
                    Name = itemView.FindViewById<TextView>(Resource.Id.textName);
                    Meta = itemView.FindViewById<TextView>(Resource.Id.textMeta);
                    RestoreHint = itemView.FindViewById<TextView>(Resource.Id.textRestoreHint);
                    Delete = itemView.FindViewById<ImageButton>(Resource.Id.buttonDelete);
                }

                public TextView Name { get; }
                public TextView Meta { get; }
                public TextView RestoreHint { get; }
                public ImageButton Delete { get; }
            }
        }
    }
}
