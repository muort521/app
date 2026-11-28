// Copyright (C) 2026 Stratum fork contributors
// SPDX-License-Identifier: GPL-3.0-only

using Android.Content;
using Stratum.WearOS.Storage;

namespace Stratum.WearOS.WebDav
{
    /// <summary>
    /// WebDAV settings stored locally on the watch. The password is kept in
    /// Android Keystore backed storage instead of plain SharedPreferences.
    /// </summary>
    public sealed class WearWebDavConfig
    {
        private const string PreferencesName = "stratum_webdav";
        private const string UrlKey = "url";
        private const string DirectoryKey = "directory";
        private const string UsernameKey = "username";
        private const string TrustAllKey = "trustAll";
        private const string EnabledKey = "enabled";
        private const string LastStatusKey = "lastStatus";
        private const string PasswordKey = "webdavPassword";

        private readonly ISharedPreferences _preferences;
        private readonly WearSecureStorage _secureStorage;

        public WearWebDavConfig(Context context)
        {
            _preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private);
            _secureStorage = new WearSecureStorage(context);
        }

        public bool Enabled
        {
            get => _preferences.GetBoolean(EnabledKey, false);
            set => _preferences.Edit().PutBoolean(EnabledKey, value).Commit();
        }

        public string Url
        {
            get => _preferences.GetString(UrlKey, null);
            set => _preferences.Edit().PutString(UrlKey, value?.Trim()).Commit();
        }

        public string Directory
        {
            get
            {
                var value = _preferences.GetString(DirectoryKey, "Stratum");
                return string.IsNullOrWhiteSpace(value) ? "Stratum" : value.Trim().Trim('/');
            }
            set => _preferences.Edit().PutString(DirectoryKey, value?.Trim().Trim('/')).Commit();
        }

        public string Username
        {
            get => _preferences.GetString(UsernameKey, null);
            set => _preferences.Edit().PutString(UsernameKey, value).Commit();
        }

        public string Password
        {
            get => _secureStorage.Get(PasswordKey);
            set => _secureStorage.Set(PasswordKey, value);
        }

        public bool TrustAllCertificates
        {
            get => _preferences.GetBoolean(TrustAllKey, false);
            set => _preferences.Edit().PutBoolean(TrustAllKey, value).Commit();
        }

        public string LastStatus
        {
            get => _preferences.GetString(LastStatusKey, null);
            set => _preferences.Edit().PutString(LastStatusKey, value).Commit();
        }

        public bool HasServer => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(Username);

        public bool IsConfigured => Enabled && HasServer;

        public string GetSummary()
        {
            if (!Enabled) return "WebDAV disabled";
            if (!HasServer) return "WebDAV not configured";
            return $"{Url?.TrimEnd('/')}/{Directory}";
        }
    }
}
