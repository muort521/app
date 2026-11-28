// Copyright (C) 2026 Stratum fork contributors
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Android.Content;
using Stratum.Core.Backup;
using Stratum.Core.Backup.Encryption;
using Stratum.Core.Entity;
using Stratum.Droid.Shared.Wear;
using Stratum.WearOS.Cache;

namespace Stratum.WearOS.WebDav
{
    /// <summary>
    /// Converts the synced watch cache back into Stratum entities and produces
    /// either an encrypted .stratum archive or a migration export.
    /// </summary>
    public static class WearBackupFactory
    {
        public static List<Authenticator> ToAuthenticators(IEnumerable<WearAuthenticator> items)
        {
            return (items ?? Array.Empty<WearAuthenticator>())
                .Where(item => !string.IsNullOrEmpty(item.Secret))
                .Select(item => new Authenticator
                {
                    Type = item.Type,
                    Secret = item.Secret,
                    Pin = item.Pin,
                    Icon = item.Icon,
                    Issuer = item.Issuer,
                    Username = item.Username,
                    Period = item.Period,
                    Digits = item.Digits,
                    Counter = item.Counter,
                    Algorithm = item.Algorithm,
                    Ranking = item.Ranking,
                    CopyCount = item.CopyCount
                })
                .ToList();
        }

        public static async Task<byte[]> CreateEncryptedBackupAsync(AuthenticatorCache authCache,
            CategoryCache categoryCache, CustomIconCache iconCache, string password)
        {
            if (authCache == null) throw new ArgumentNullException(nameof(authCache));
            if (string.IsNullOrEmpty(password)) throw new ArgumentException("Password is required");

            var authenticators = ToAuthenticators(authCache.GetItems());
            if (authenticators.Count == 0)
            {
                throw new InvalidOperationException("No authenticators to back up");
            }

            var categories = categoryCache?.GetItems()
                .Select(category => new Category
                {
                    Id = category.Id,
                    Name = category.Name,
                    Ranking = category.Ranking
                })
                .ToList() ?? new List<Category>();

            var bindings = new List<AuthenticatorCategory>();
            foreach (var auth in authCache.GetItems())
            {
                if (auth.Categories == null)
                {
                    continue;
                }

                bindings.AddRange(auth.Categories.Select(category => new AuthenticatorCategory(auth.Secret,
                    category.CategoryId)
                {
                    Ranking = category.Ranking
                }));
            }

            var customIcons = new List<CustomIcon>();
            if (iconCache != null)
            {
                foreach (var id in iconCache.GetIcons())
                {
                    var data = iconCache.GetData(id);
                    if (data != null && data.Length > 0)
                    {
                        customIcons.Add(new CustomIcon { Id = id, Data = data });
                    }
                }
            }

            var backup = new Backup
            {
                Authenticators = authenticators,
                Categories = categories,
                AuthenticatorCategories = bindings,
                CustomIcons = customIcons
            };

            return await new StrongBackupEncryption().EncryptAsync(backup, password);
        }

        public static async Task<Backup> DecryptBackupAsync(byte[] data, string password)
        {
            if (data == null || data.Length == 0)
            {
                throw new InvalidOperationException("Backup file is empty");
            }

            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentException("Password is required");
            }

            Exception lastError = null;
            var encryptions = new IBackupEncryption[]
            {
                new StrongBackupEncryption(),
                new LegacyBackupEncryption(),
                new NoBackupEncryption()
            };

            foreach (var encryption in encryptions)
            {
                if (!encryption.CanBeDecrypted(data))
                {
                    continue;
                }

                try
                {
                    var backup = await encryption.DecryptAsync(data, password);
                    if (backup?.Authenticators != null)
                    {
                        return backup;
                    }
                }
                catch (Exception e)
                {
                    lastError = e;
                }
            }

            throw new InvalidOperationException("The password is incorrect or the backup format is not supported",
                lastError);
        }

        /// <summary>
        /// Applies a decrypted backup to the watch's local cache. The phone also
        /// receives the same backup so the restore survives the next phone sync.
        /// </summary>
        public static async Task ApplyRestoreAsync(Backup backup, AuthenticatorCache authCache,
            CategoryCache categoryCache, CustomIconCache iconCache)
        {
            if (backup == null) throw new ArgumentNullException(nameof(backup));
            if (authCache == null) throw new ArgumentNullException(nameof(authCache));

            var bindings = backup.AuthenticatorCategories?.ToList() ?? new List<AuthenticatorCategory>();
            var authenticators = (backup.Authenticators ?? Array.Empty<Authenticator>())
                .Select(item => new WearAuthenticator
                {
                    Type = item.Type,
                    Secret = item.Secret,
                    Pin = item.Pin,
                    Icon = item.Icon,
                    Issuer = item.Issuer,
                    Username = item.Username,
                    Period = item.Period,
                    Digits = item.Digits,
                    Counter = item.Counter,
                    Algorithm = item.Algorithm,
                    Ranking = item.Ranking,
                    CopyCount = item.CopyCount,
                    Categories = bindings
                        .Where(binding => binding.AuthenticatorSecret == item.Secret)
                        .Select(binding => new WearAuthenticatorCategory
                        {
                            CategoryId = binding.CategoryId,
                            Ranking = binding.Ranking
                        })
                        .ToList()
                })
                .ToList();

            var categories = (backup.Categories ?? Array.Empty<Category>())
                .Select(category => new WearCategory
                {
                    Id = category.Id,
                    Name = category.Name,
                    Ranking = category.Ranking
                })
                .ToList();

            await authCache.ReplaceAsync(authenticators);

            if (categoryCache != null)
            {
                await categoryCache.ReplaceAsync(categories);
            }

            if (iconCache != null)
            {
                var newIcons = (backup.CustomIcons ?? Array.Empty<CustomIcon>())
                    .Where(icon => icon != null && !string.IsNullOrEmpty(icon.Id) && icon.Data != null)
                    .ToList();
                var newIds = newIcons.Select(icon => icon.Id).ToHashSet(StringComparer.Ordinal);

                foreach (var oldId in iconCache.GetIcons())
                {
                    if (!newIds.Contains(oldId))
                    {
                        iconCache.Remove(oldId);
                    }
                }

                foreach (var icon in newIcons)
                {
                    await iconCache.AddAsync(icon.Id, icon.Data);
                }
            }
        }

        public static byte[] CreateAegisExport(IEnumerable<WearAuthenticator> items)
        {
            return Encoding.UTF8.GetBytes(MigrationExport.ToAegisJson(ToAuthenticators(items)));
        }

        public static byte[] CreateTwoFasExport(IEnumerable<WearAuthenticator> items)
        {
            return Encoding.UTF8.GetBytes(MigrationExport.ToTwoFasJson(ToAuthenticators(items)));
        }

        public static byte[] CreateBitwardenJsonExport(IEnumerable<WearAuthenticator> items)
        {
            return Encoding.UTF8.GetBytes(MigrationExport.ToBitwardenJson(ToAuthenticators(items)));
        }

        public static byte[] CreateBitwardenCsvExport(IEnumerable<WearAuthenticator> items)
        {
            return Encoding.UTF8.GetBytes(MigrationExport.ToBitwardenCsv(ToAuthenticators(items)));
        }

        public static string EncryptedFileName()
        {
            return $"stratum-{DateTime.Now:yyyyMMdd-HHmmss}.stratum";
        }

        public static string ExportFileName(string prefix, string extension)
        {
            return $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}";
        }

        /// <summary>
        /// The watch only creates backups in memory and uploads them directly to
        /// WebDAV. This removes transient files from older runs without touching
        /// the normal account cache (authenticators.json/categories.json).
        /// </summary>
        public static void ClearTransientBackupArtifacts(Context context)
        {
            var cacheDir = context?.CacheDir?.Path;
            if (string.IsNullOrWhiteSpace(cacheDir) || !Directory.Exists(cacheDir))
            {
                return;
            }

            DeleteMatchingFiles(cacheDir, "stratum-*.stratum");
            DeleteMatchingFiles(cacheDir, "aegis-*.json");
            DeleteMatchingFiles(cacheDir, "2fas-*.2fas");
            DeleteMatchingFiles(cacheDir, "bitwarden-*.json");
            DeleteMatchingFiles(cacheDir, "bitwarden-*.csv");
            DeleteMatchingFiles(cacheDir, "*.tmp");
        }

        private static void DeleteMatchingFiles(string directory, string pattern)
        {
            try
            {
                foreach (var path in Directory.GetFiles(directory, pattern))
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch
                    {
                        // A transient file is not worth failing an upload over.
                    }
                }
            }
            catch
            {
                // Cache cleanup is best-effort only.
            }
        }
    }

    public static class WearWebDavService
    {
        public static async Task TestAsync(Context context)
        {
            var config = new WearWebDavConfig(context);
            if (!config.HasServer)
            {
                throw new InvalidOperationException("WebDAV is not configured");
            }

            using var client = new WearWebDavClient(config);
            await client.TestAsync();
        }

        public static async Task<List<WebDavEntry>> ListAsync(Context context)
        {
            var config = new WearWebDavConfig(context);
            if (!config.IsConfigured)
            {
                throw new InvalidOperationException("WebDAV is not enabled or configured");
            }

            using var client = new WearWebDavClient(config);
            return await client.ListAsync();
        }

        public static async Task<string> UploadAsync(Context context, string fileName, byte[] data)
        {
            var config = new WearWebDavConfig(context);
            if (!config.IsConfigured)
            {
                throw new InvalidOperationException("WebDAV is not enabled or configured");
            }

            using var client = new WearWebDavClient(config);
            await client.UploadAsync(fileName, data);

            config.LastStatus = $"Uploaded {fileName}";
            return fileName;
        }

        public static async Task<byte[]> DownloadAsync(Context context, WebDavEntry entry)
        {
            var config = new WearWebDavConfig(context);
            if (!config.IsConfigured)
            {
                throw new InvalidOperationException("WebDAV is not enabled or configured");
            }

            using var client = new WearWebDavClient(config);
            return await client.DownloadAsync(entry);
        }

        public static async Task DeleteAsync(Context context, WebDavEntry entry)
        {
            var config = new WearWebDavConfig(context);
            if (!config.IsConfigured)
            {
                throw new InvalidOperationException("WebDAV is not enabled or configured");
            }

            using var client = new WearWebDavClient(config);
            await client.DeleteAsync(entry);
            config.LastStatus = $"Deleted {entry.Name}";
        }
    }
}
