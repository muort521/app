// Copyright (C) 2026 Stratum fork contributors
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using SimpleBase;
using Stratum.Core;
using Stratum.Core.Entity;
using Stratum.Core.Generator;

namespace Stratum.Core.Backup
{
    /// <summary>
    /// Exports authenticators in formats accepted by other authenticator apps.
    /// These files are deliberately unencrypted and only contain the account
    /// data needed for migration; categories and icons are not included.
    /// </summary>
    public static class MigrationExport
    {
        public const string JsonMimeType = "application/json";
        public const string AegisFileExtension = "json";
        public const string TwoFasFileExtension = "2fas";
        public const string BitwardenJsonFileExtension = "json";
        public const string BitwardenCsvFileExtension = "csv";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static string ToAegisJson(IEnumerable<Authenticator> authenticators)
        {
            var entries = authenticators.Select(auth => new
            {
                type = GetAegisType(auth.Type),
                uuid = Guid.NewGuid().ToString(),
                name = auth.Username ?? string.Empty,
                issuer = auth.Issuer,
                group = (string) null,
                icon = (object) null,
                info = new
                {
                    secret = GetAegisSecret(auth),
                    algo = GetAegisAlgorithm(auth),
                    digits = auth.Digits,
                    period = auth.Period,
                    counter = auth.Counter,
                    pin = auth.Pin
                }
            }).ToList();

            var backup = new
            {
                version = 1,
                header = new
                {
                    slots = (object) null,
                    @params = (object) null
                },
                db = new
                {
                    version = 3,
                    entries,
                    groups = Array.Empty<object>()
                }
            };

            return JsonSerializer.Serialize(backup, JsonOptions);
        }

        public static string ToTwoFasJson(IEnumerable<Authenticator> authenticators)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;
            var services = new List<object>();
            var position = 0;

            foreach (var auth in authenticators)
            {
                if (!TryGetTwoFasTokenType(auth.Type, out var tokenType))
                {
                    continue;
                }

                services.Add(new
                {
                    name = auth.Issuer,
                    secret = auth.Secret,
                    updatedAt = timestamp,
                    otp = new
                    {
                        link = auth.GetUri(),
                        label = string.IsNullOrEmpty(auth.Username) ? auth.Issuer : auth.Username,
                        account = auth.Username ?? string.Empty,
                        issuer = auth.Issuer,
                        tokenType,
                        source = "Link",
                        algorithm = GetAlgorithmName(auth.Algorithm),
                        digits = auth.Digits,
                        period = auth.Period,
                        counter = auth.Counter
                    },
                    order = new { position = position++ }
                });
            }

            var backup = new
            {
                services,
                groups = Array.Empty<object>(),
                updatedAt = timestamp,
                schemaVersion = 4,
                appVersionCode = 5000022,
                appVersionName = "5.4.5"
            };

            return JsonSerializer.Serialize(backup, JsonOptions);
        }

        public static string ToBitwardenJson(IEnumerable<Authenticator> authenticators)
        {
            var items = authenticators
                .Where(auth => auth.Type == AuthenticatorType.Totp)
                .Select(auth => new
                {
                    id = Guid.NewGuid().ToString(),
                    organizationId = (string) null,
                    folderId = (string) null,
                    type = 1,
                    reprompt = 0,
                    name = auth.Issuer,
                    notes = (string) null,
                    favorite = false,
                    fields = Array.Empty<object>(),
                    login = new
                    {
                        uris = Array.Empty<object>(),
                        username = auth.Username,
                        password = (string) null,
                        totp = auth.GetUri()
                    },
                    collectionIds = (string) null
                }).ToList();

            var vault = new
            {
                encrypted = false,
                folders = Array.Empty<object>(),
                items
            };

            return JsonSerializer.Serialize(vault, JsonOptions);
        }

        public static string ToBitwardenCsv(IEnumerable<Authenticator> authenticators)
        {
            var builder = new StringBuilder();
            builder.AppendLine("folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp");

            foreach (var auth in authenticators.Where(auth => auth.Type == AuthenticatorType.Totp))
            {
                var fields = new[]
                {
                    string.Empty, // folder
                    "0", // favorite
                    "login", // type
                    auth.Issuer,
                    string.Empty, // notes
                    string.Empty, // fields
                    "0", // reprompt
                    string.Empty, // login_uri
                    auth.Username ?? string.Empty,
                    string.Empty, // login_password
                    auth.GetUri()
                };

                builder.AppendLine(string.Join(",", fields.Select(EscapeCsvField)));
            }

            return builder.ToString();
        }

        private static string EscapeCsvField(string value)
        {
            if (value.Length > 0 && (value.Contains(',') || value.Contains('"') || value.Contains('\r') ||
                                     value.Contains('\n')))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }

        private static string GetAegisType(AuthenticatorType type)
        {
            return type switch
            {
                AuthenticatorType.Totp => "totp",
                AuthenticatorType.Hotp => "hotp",
                AuthenticatorType.SteamOtp => "steam",
                AuthenticatorType.MobileOtp => "motp",
                AuthenticatorType.YandexOtp => "yandex",
                _ => throw new ArgumentOutOfRangeException(nameof(type))
            };
        }

        private static bool TryGetTwoFasTokenType(AuthenticatorType type, out string tokenType)
        {
            tokenType = type switch
            {
                AuthenticatorType.Totp => "TOTP",
                AuthenticatorType.Hotp => "HOTP",
                AuthenticatorType.SteamOtp => "STEAM",
                _ => null
            };

            return tokenType != null;
        }

        private static string GetAegisSecret(Authenticator auth)
        {
            if (auth.Type != AuthenticatorType.MobileOtp)
            {
                return auth.Secret;
            }

            // Aegis stores MOTP secrets as Base32; Stratum uses hex for this type.
            var hex = auth.Secret.Replace(" ", string.Empty).Replace("-", string.Empty);
            return Base32.Rfc4648.Encode(Convert.FromHexString(hex));
        }

        private static string GetAegisAlgorithm(Authenticator auth)
        {
            // Aegis has fixed algorithm requirements for its special OTP types.
            return auth.Type switch
            {
                AuthenticatorType.MobileOtp => "MD5",
                AuthenticatorType.SteamOtp => "SHA1",
                AuthenticatorType.YandexOtp => "SHA256",
                _ => GetAlgorithmName(auth.Algorithm)
            };
        }

        private static string GetAlgorithmName(HashAlgorithm algorithm)
        {
            return algorithm switch
            {
                HashAlgorithm.Sha1 => "SHA1",
                HashAlgorithm.Sha256 => "SHA256",
                HashAlgorithm.Sha512 => "SHA512",
                _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
            };
        }
    }
}
