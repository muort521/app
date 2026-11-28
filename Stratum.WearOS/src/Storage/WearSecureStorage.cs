// Copyright (C) 2026 Stratum fork contributors
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Text;
using Android.Content;
using Android.Runtime;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;

namespace Stratum.WearOS.Storage
{
    /// <summary>
    /// Minimal Android Keystore backed string storage for the watch.
    /// The phone implementation depends on Serilog and older API fallbacks;
    /// this copy only needs AES/GCM on API 26+.
    /// </summary>
    public sealed class WearSecureStorage
    {
        private const string KeyStoreName = "AndroidKeyStore";
        private const string CipherName = "AES/GCM/NoPadding";
        private const int IvLength = 12;
        private const int TagBits = 128;

        private readonly ISharedPreferences _preferences;
        private readonly string _keyAlias;
        private readonly object _lock = new object();

        public WearSecureStorage(Context context)
        {
            _preferences = context.GetSharedPreferences(
                $"{context.PackageName}_webdav_secure", FileCreationMode.Private);
            _keyAlias = $"{context.PackageName}_webdav";
        }

        public string Get(string key)
        {
            var encoded = _preferences.GetString(key, null);
            if (string.IsNullOrEmpty(encoded))
            {
                return null;
            }

            try
            {
                return Decrypt(Convert.FromBase64String(encoded));
            }
            catch (Exception e)
            {
                Logger.Error(e);
                _preferences.Edit().Remove(key).Commit();
                return null;
            }
        }

        public void Set(string key, string value)
        {
            if (value == null)
            {
                _preferences.Edit().Remove(key).Commit();
                return;
            }

            var encrypted = Encrypt(value);
            _preferences.Edit().PutString(key, Convert.ToBase64String(encrypted)).Commit();
        }

        private byte[] Encrypt(string value)
        {
            var key = GetOrCreateKey();
            var iv = new byte[IvLength];
            new SecureRandom().NextBytes(iv);

            var cipher = Cipher.GetInstance(CipherName);
            cipher.Init(CipherMode.EncryptMode, key, new GCMParameterSpec(TagBits, iv));

            var encrypted = cipher.DoFinal(Encoding.UTF8.GetBytes(value));
            var result = new byte[IvLength + encrypted.Length];
            Buffer.BlockCopy(iv, 0, result, 0, IvLength);
            Buffer.BlockCopy(encrypted, 0, result, IvLength, encrypted.Length);
            return result;
        }

        private string Decrypt(byte[] data)
        {
            if (data.Length <= IvLength)
            {
                return null;
            }

            var key = GetOrCreateKey();
            var iv = new byte[IvLength];
            Buffer.BlockCopy(data, 0, iv, 0, IvLength);

            var cipher = Cipher.GetInstance(CipherName);
            cipher.Init(CipherMode.DecryptMode, key, new GCMParameterSpec(TagBits, iv));

            var decrypted = cipher.DoFinal(data, IvLength, data.Length - IvLength);
            return Encoding.UTF8.GetString(decrypted);
        }

        private ISecretKey GetOrCreateKey()
        {
            lock (_lock)
            {
                var keyStore = KeyStore.GetInstance(KeyStoreName);
                keyStore.Load(null);

                var existing = keyStore.GetKey(_keyAlias, null);
                if (existing != null)
                {
                    return existing.JavaCast<ISecretKey>();
                }

                var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, KeyStoreName);
                var spec = new KeyGenParameterSpec.Builder(_keyAlias,
                        KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
                    .SetBlockModes(KeyProperties.BlockModeGcm)
                    .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
                    .SetRandomizedEncryptionRequired(false)
                    .Build();

                generator.Init(spec);
                return generator.GenerateKey();
            }
        }
    }
}
