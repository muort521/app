// Copyright (C) 2026 Stratum fork contributors
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using AndroidX.AppCompat.App;
using Stratum.WearOS.Cache;
using Stratum.WearOS.Util;
using Stratum.WearOS.WebDav;
using AlertDialog = AndroidX.AppCompat.App.AlertDialog;

namespace Stratum.WearOS.Activity
{
    [Activity(Label = "@string/webdavTitle", Exported = false)]
    public class WebDavActivity : AsyncActivity
    {
        private WearWebDavConfig _config;
        private AuthenticatorCache _authCache;
        private CategoryCache _categoryCache;
        private CustomIconCache _iconCache;

        private CheckBox _checkEnabled;
        private CheckBox _checkTrustAll;
        private EditText _editUrl;
        private EditText _editDirectory;
        private EditText _editUsername;
        private EditText _editPassword;
        private TextView _textStatus;
        private Button _buttonSave;
        private Button _buttonTest;
        private Button _buttonBackup;
        private Button _buttonExport;
        private Button _buttonCloudList;
        private Button _buttonBack;

        private bool _isBusy;

        protected override async Task OnCreateAsync()
        {
            SetTheme(Resource.Style.AppTheme);
            SetContentView(Resource.Layout.activityWebDav);
            Window?.SetSoftInputMode(SoftInput.AdjustResize);

            _config = new WearWebDavConfig(this);
            _authCache = Dependencies.Resolve<AuthenticatorCache>();
            _categoryCache = Dependencies.Resolve<CategoryCache>();
            _iconCache = Dependencies.Resolve<CustomIconCache>();

            await Task.WhenAll(_authCache.InitAsync(), _categoryCache.InitAsync(), _iconCache.InitAsync());

            _checkEnabled = FindViewById<CheckBox>(Resource.Id.checkEnabled);
            _checkTrustAll = FindViewById<CheckBox>(Resource.Id.checkTrustAll);
            _editUrl = FindViewById<EditText>(Resource.Id.editUrl);
            _editDirectory = FindViewById<EditText>(Resource.Id.editDirectory);
            _editUsername = FindViewById<EditText>(Resource.Id.editUsername);
            _editPassword = FindViewById<EditText>(Resource.Id.editPassword);
            _textStatus = FindViewById<TextView>(Resource.Id.textStatus);
            _buttonSave = FindViewById<Button>(Resource.Id.buttonSave);
            _buttonTest = FindViewById<Button>(Resource.Id.buttonTest);
            _buttonBackup = FindViewById<Button>(Resource.Id.buttonBackup);
            _buttonExport = FindViewById<Button>(Resource.Id.buttonExport);
            _buttonCloudList = FindViewById<Button>(Resource.Id.buttonCloudList);
            _buttonBack = FindViewById<Button>(Resource.Id.buttonBack);

            // Do not add a swipe-to-dismiss callback here: the page scrolls vertically and
            // a downward scroll at the top of the list would otherwise close the activity.

            _buttonSave.Click += delegate { SaveConfig(); };
            _buttonTest.Click += async delegate { await TestConnectionAsync(); };
            _buttonBackup.Click += delegate { PromptBackupPassword(); };
            _buttonExport.Click += delegate { ShowExportFormats(); };
            _buttonCloudList.Click += delegate
            {
                StartActivity(new Intent(this, typeof(WebDavListActivity)));
            };
            _buttonBack.Click += delegate { Finish(); };

            LoadConfig();
            RequestLocalNetworkPermissionIfNeeded();
        }

        private void RequestLocalNetworkPermissionIfNeeded()
        {
            const string permission = "android.permission.ACCESS_LOCAL_NETWORK";

            if ((int) Build.VERSION.SdkInt < 36)
            {
                return;
            }

            if (!IsLocalHost(_editUrl.Text))
            {
                return;
            }

            if (CheckSelfPermission(permission) == Permission.Granted)
            {
                return;
            }

            RequestPermissions(new[] { permission }, 1001);
        }

        private static bool IsLocalHost(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            string host;
            try
            {
                var uri = new Uri(url.Contains("://") ? url : "https://" + url, UriKind.Absolute);
                host = uri.Host;
            }
            catch
            {
                return false;
            }

            return host == "localhost"
                   || host == "::1"
                   || host.StartsWith("127.")
                   || host.StartsWith("10.")
                   || host.StartsWith("192.168.")
                   || IsPrivate172(host);
        }

        private static bool IsPrivate172(string host)
        {
            if (!host.StartsWith("172."))
            {
                return false;
            }

            var parts = host.Split('.');
            return parts.Length >= 2 && int.TryParse(parts[1], out var second) && second >= 16 && second <= 31;
        }

        protected override Task OnResumeAsync()
        {
            if (_textStatus != null && _config != null)
            {
                _textStatus.Text = _config.LastStatus ?? "";
            }

            return Task.CompletedTask;
        }

        private void LoadConfig()
        {
            _checkEnabled.Checked = _config.Enabled;
            _checkTrustAll.Checked = _config.TrustAllCertificates;
            _editUrl.Text = _config.Url;
            _editDirectory.Text = _config.Directory;
            _editUsername.Text = _config.Username;
            _editPassword.Text = _config.Password;
            _textStatus.Text = _config.LastStatus ?? "";
        }

        private void SaveConfig()
        {
            _config.Enabled = _checkEnabled.Checked;
            _config.Url = _editUrl.Text;
            _config.Directory = _editDirectory.Text;
            _config.Username = _editUsername.Text;
            if (!string.IsNullOrEmpty(_editPassword.Text))
            {
                _config.Password = _editPassword.Text;
            }
            _config.TrustAllCertificates = _checkTrustAll.Checked;
            _textStatus.SetText(Resource.String.webdavSaved);
            HideKeyboard();
            RequestLocalNetworkPermissionIfNeeded();
        }

        private async Task TestConnectionAsync()
        {
            if (_isBusy) return;
            SaveConfig();

            if (!_config.HasServer)
            {
                ShowToast(GetString(Resource.String.webdavNotConfigured));
                return;
            }

            SetBusy(true);
            _textStatus.SetText(Resource.String.webdavTesting);

            try
            {
                await WearWebDavService.TestAsync(this);
                _config.LastStatus = GetString(Resource.String.webdavTestOk);
                _textStatus.SetText(Resource.String.webdavTestOk);
            }
            catch (Exception e)
            {
                _config.LastStatus = GetString(Resource.String.webdavTestFailed, ErrorMessage(e));
                _textStatus.Text = _config.LastStatus;
                ShowToast(_config.LastStatus);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void PromptBackupPassword()
        {
            if (_isBusy) return;
            SaveConfig();

            if (!EnsureConfigured()) return;
            if (_authCache.GetItems().Count == 0)
            {
                ShowToast(GetString(Resource.String.webdavNoAuthenticators));
                return;
            }

            var input = new EditText(this);
            input.Hint = GetString(Resource.String.webdavBackupPasswordHint);
            input.InputType = InputTypes.ClassText | InputTypes.TextVariationPassword;
            input.SetSingleLine(true);
            input.SetTextColor(Android.Graphics.Color.White);
            input.LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, WearDialog.Dp(this, 40));

            var container = WearDialog.CreateContainer(this);
            container.AddView(WearDialog.CreateTitle(this, GetString(Resource.String.webdavBackupPasswordTitle)));
            container.AddView(input);

            var cancel = WearDialog.CreateButton(this, GetString(Resource.String.webdavCancel));
            var confirm = WearDialog.CreateButton(this, GetString(Resource.String.webdavBackupEncrypted));
            container.AddView(WearDialog.CreateButtonRow(this, cancel, confirm));

            var dialog = WearDialog.Show(this, container);

            cancel.Click += delegate { dialog.Dismiss(); };
            confirm.Click += delegate
            {
                var password = input.Text;
                if (string.IsNullOrEmpty(password))
                {
                    ShowToast(GetString(Resource.String.webdavBackupPasswordEmpty));
                    return;
                }

                dialog.Dismiss();
                _ = BackupAsync(password);
            };
        }

        private async Task BackupAsync(string password)
        {
            if (_isBusy) return;
            SetBusy(true);
            _textStatus.SetText(Resource.String.webdavBackupUploading);

            byte[] data = null;

            try
            {
                data = await WearBackupFactory.CreateEncryptedBackupAsync(_authCache, _categoryCache,
                    _iconCache, password);
                var fileName = WearBackupFactory.EncryptedFileName();
                await WearWebDavService.UploadAsync(this, fileName, data);

                _config.LastStatus = GetString(Resource.String.webdavBackupSuccess, fileName);
                _textStatus.Text = _config.LastStatus;
            }
            catch (Exception e)
            {
                _config.LastStatus = GetString(Resource.String.webdavBackupFailed, ErrorMessage(e));
                _textStatus.Text = _config.LastStatus;
                ShowToast(_config.LastStatus);
            }
            finally
            {
                if (data != null)
                {
                    Array.Clear(data, 0, data.Length);
                }

                WearBackupFactory.ClearTransientBackupArtifacts(this);
                SetBusy(false);
            }
        }

        private void ShowExportFormats()
        {
            if (_isBusy) return;
            SaveConfig();

            if (!EnsureConfigured()) return;
            if (_authCache.GetItems().Count == 0)
            {
                ShowToast(GetString(Resource.String.webdavNoAuthenticators));
                return;
            }

            var container = WearDialog.CreateContainer(this);
            container.AddView(WearDialog.CreateTitle(this, GetString(Resource.String.webdavExportSelect)));

            var options = new LinearLayout(this);
            options.Orientation = Orientation.Vertical;

            var scroll = new ScrollView(this);
            scroll.VerticalScrollBarEnabled = false;
            scroll.LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, WearDialog.Dp(this, 96));
            scroll.AddView(options);

            string[] labels;
            try
            {
                labels = Resources.GetStringArray(Resource.Array.webdavExportFormats);
            }
            catch
            {
                labels = new[]
                {
                    GetString(Resource.String.webdavExportAegis),
                    GetString(Resource.String.webdavExportTwoFas),
                    GetString(Resource.String.webdavExportBitwardenJson),
                    GetString(Resource.String.webdavExportBitwardenCsv)
                };
            }

            var dialog = (AlertDialog) null;

            for (var i = 0; i < labels.Length; i++)
            {
                var index = i;
                var row = new TextView(this);
                row.Text = labels[i];
                row.SetTextSize(Android.Util.ComplexUnitType.Sp, 10);
                row.SetTextColor(Android.Graphics.Color.White);
                row.Gravity = GravityFlags.CenterVertical;
                row.SetPadding(WearDialog.Dp(this, 6), 0, WearDialog.Dp(this, 6), 0);
                row.LayoutParameters = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent, WearDialog.Dp(this, 24));
                row.Clickable = true;
                row.Focusable = true;
                row.Click += delegate
                {
                    dialog?.Dismiss();
                    ShowExportWarning(index);
                };
                options.AddView(row);
            }

            container.AddView(scroll);

            var cancel = WearDialog.CreateButton(this, GetString(Resource.String.webdavCancel));
            cancel.LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, WearDialog.Dp(this, 34));
            container.AddView(cancel);

            dialog = WearDialog.Show(this, container);
            cancel.Click += delegate { dialog.Dismiss(); };
        }

        private void ShowExportWarning(int format)
        {
            var container = WearDialog.CreateContainer(this);
            container.AddView(WearDialog.CreateTitle(this, GetString(Resource.String.webdavExportWarningTitle)));
            container.AddView(WearDialog.CreateMessage(this, GetString(Resource.String.webdavExportWarning)));

            var cancel = WearDialog.CreateButton(this, GetString(Resource.String.webdavCancel));
            var confirm = WearDialog.CreateButton(this, GetString(Resource.String.webdavExport));
            container.AddView(WearDialog.CreateButtonRow(this, cancel, confirm));

            var dialog = WearDialog.Show(this, container);

            cancel.Click += delegate { dialog.Dismiss(); };
            confirm.Click += delegate
            {
                dialog.Dismiss();
                _ = ExportAsync(format);
            };
        }

        private async Task ExportAsync(int format)
        {
            if (_isBusy) return;
            SetBusy(true);
            _textStatus.SetText(Resource.String.webdavExporting);

            byte[] data = null;
            string fileName = null;

            try
            {
                switch (format)
                {
                    case 0:
                        data = WearBackupFactory.CreateAegisExport(_authCache.GetItems());
                        fileName = WearBackupFactory.ExportFileName("aegis",
                            Stratum.Core.Backup.MigrationExport.AegisFileExtension);
                        break;

                    case 1:
                        data = WearBackupFactory.CreateTwoFasExport(_authCache.GetItems());
                        fileName = WearBackupFactory.ExportFileName("2fas",
                            Stratum.Core.Backup.MigrationExport.TwoFasFileExtension);
                        break;

                    case 2:
                        data = WearBackupFactory.CreateBitwardenJsonExport(_authCache.GetItems());
                        fileName = WearBackupFactory.ExportFileName("bitwarden",
                            Stratum.Core.Backup.MigrationExport.BitwardenJsonFileExtension);
                        break;

                    default:
                        data = WearBackupFactory.CreateBitwardenCsvExport(_authCache.GetItems());
                        fileName = WearBackupFactory.ExportFileName("bitwarden",
                            Stratum.Core.Backup.MigrationExport.BitwardenCsvFileExtension);
                        break;
                }

                await WearWebDavService.UploadAsync(this, fileName, data);

                _config.LastStatus = GetString(Resource.String.webdavExportSuccess, fileName);
                _textStatus.Text = _config.LastStatus;
            }
            catch (Exception e)
            {
                _config.LastStatus = GetString(Resource.String.webdavExportFailed, ErrorMessage(e));
                _textStatus.Text = _config.LastStatus;
                ShowToast(_config.LastStatus);
            }
            finally
            {
                if (data != null)
                {
                    Array.Clear(data, 0, data.Length);
                }

                WearBackupFactory.ClearTransientBackupArtifacts(this);
                SetBusy(false);
            }
        }

        private bool EnsureConfigured()
        {
            if (_config.IsConfigured)
            {
                return true;
            }

            ShowToast(GetString(Resource.String.webdavNotConfigured));
            return false;
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            _buttonSave.Enabled = !busy;
            _buttonTest.Enabled = !busy;
            _buttonBackup.Enabled = !busy;
            _buttonExport.Enabled = !busy;
            _buttonCloudList.Enabled = !busy;
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

        private void HideKeyboard()
        {
            var manager = (InputMethodManager) GetSystemService(Context.InputMethodService);
            manager?.HideSoftInputFromWindow(_editPassword.WindowToken, 0);
        }
    }
}
