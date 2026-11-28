// Copyright (C) 2026 Stratum fork contributors
// SPDX-License-Identifier: GPL-3.0-only

using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Moq;
using Stratum.Core;
using Stratum.Core.Backup;
using Stratum.Core.Converter;
using Stratum.Core.Entity;
using Stratum.Core.Generator;
using Xunit;

namespace Stratum.Test.Backup
{
    public class MigrationExportTest
    {
        private readonly Authenticator _totp = new()
        {
            Type = AuthenticatorType.Totp,
            Issuer = "Example, Inc.",
            Username = "user\"name",
            Secret = "JBSWY3DPEHPK3PXP",
            Algorithm = HashAlgorithm.Sha1,
            Digits = 6,
            Period = 30
        };

        private static IIconResolver CreateIconResolver()
        {
            var resolver = new Mock<IIconResolver>();
            resolver.Setup(r => r.FindServiceKeyByName(It.IsAny<string>())).Returns("icon");
            return resolver.Object;
        }

        [Fact]
        public async Task AegisJson_CanBeImported()
        {
            var json = MigrationExport.ToAegisJson([_totp]);
            var converter = new AegisBackupConverter(CreateIconResolver(), Mock.Of<ICustomIconDecoder>());

            var result = await converter.ConvertAsync(Encoding.UTF8.GetBytes(json));

            Assert.Empty(result.Failures);
            var auth = Assert.Single(result.Backup.Authenticators);
            Assert.Equal(AuthenticatorType.Totp, auth.Type);
            Assert.Equal(_totp.Issuer, auth.Issuer);
            Assert.Equal(_totp.Username, auth.Username);
            Assert.Equal(_totp.Secret, auth.Secret);
        }

        [Fact]
        public async Task AegisJson_MobileOtpCanBeImported()
        {
            var mobileOtp = new Authenticator
            {
                Type = AuthenticatorType.MobileOtp,
                Issuer = "Legacy",
                Secret = "0123456789abcdef",
                Pin = "1234"
            };

            var json = MigrationExport.ToAegisJson([mobileOtp]);
            var converter = new AegisBackupConverter(CreateIconResolver(), Mock.Of<ICustomIconDecoder>());
            var result = await converter.ConvertAsync(Encoding.UTF8.GetBytes(json));

            Assert.Empty(result.Failures);
            var auth = Assert.Single(result.Backup.Authenticators);
            Assert.Equal(AuthenticatorType.MobileOtp, auth.Type);
            Assert.Equal(mobileOtp.Secret, auth.Secret);
            Assert.Equal(mobileOtp.Pin, auth.Pin);
        }

        [Fact]
        public async Task TwoFasJson_CanBeImported()
        {
            var json = MigrationExport.ToTwoFasJson([_totp]);
            var converter = new TwoFasBackupConverter(CreateIconResolver());

            var result = await converter.ConvertAsync(Encoding.UTF8.GetBytes(json));

            Assert.Empty(result.Failures);
            var auth = Assert.Single(result.Backup.Authenticators);
            Assert.Equal(AuthenticatorType.Totp, auth.Type);
            Assert.Equal(_totp.Issuer, auth.Issuer);
            Assert.Equal(_totp.Username, auth.Username);
            Assert.Equal(_totp.Secret, auth.Secret);
        }

        [Fact]
        public void AegisJson_HasExpectedEnvelope()
        {
            using var document = JsonDocument.Parse(MigrationExport.ToAegisJson([_totp]));
            var root = document.RootElement;

            Assert.Equal(1, root.GetProperty("version").GetInt32());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("header").GetProperty("slots").ValueKind);
            Assert.Equal(JsonValueKind.Null, root.GetProperty("header").GetProperty("params").ValueKind);
            Assert.Equal(3, root.GetProperty("db").GetProperty("version").GetInt32());
            Assert.Equal("totp", root.GetProperty("db").GetProperty("entries")[0].GetProperty("type").GetString());
        }

        [Fact]
        public void TwoFasJson_HasExpectedEnvelope()
        {
            using var document = JsonDocument.Parse(MigrationExport.ToTwoFasJson([_totp]));
            var root = document.RootElement;

            Assert.Equal(4, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(JsonValueKind.Array, root.GetProperty("groups").ValueKind);
            var service = root.GetProperty("services")[0];
            Assert.Equal("TOTP", service.GetProperty("otp").GetProperty("tokenType").GetString());
            Assert.Equal("SHA1", service.GetProperty("otp").GetProperty("algorithm").GetString());
        }

        [Fact]
        public void BitwardenJson_HasExpectedEnvelope()
        {
            using var document = JsonDocument.Parse(MigrationExport.ToBitwardenJson([_totp]));
            var root = document.RootElement;

            Assert.False(root.GetProperty("encrypted").GetBoolean());
            var item = root.GetProperty("items")[0];
            Assert.Equal(1, item.GetProperty("type").GetInt32());
            Assert.StartsWith("otpauth://totp/", item.GetProperty("login").GetProperty("totp").GetString());
        }

        [Fact]
        public void TwoFasJson_SkipsUnsupportedTypes()
        {
            var mobileOtp = new Authenticator
            {
                Type = AuthenticatorType.MobileOtp,
                Issuer = "Legacy",
                Secret = "0123456789abcdef",
                Pin = "1234"
            };

            using var document = JsonDocument.Parse(MigrationExport.ToTwoFasJson([mobileOtp]));
            Assert.Empty(document.RootElement.GetProperty("services").EnumerateArray());
        }

        [Fact]
        public void BitwardenJson_OnlyIncludesTotp()
        {
            var hotp = new Authenticator
            {
                Type = AuthenticatorType.Hotp,
                Issuer = "Legacy HOTP",
                Secret = "JBSWY3DPEHPK3PXP",
                Digits = 6,
                Counter = 1
            };

            using var document = JsonDocument.Parse(MigrationExport.ToBitwardenJson([_totp, hotp]));
            Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        }

        [Fact]
        public async Task BitwardenJson_CanBeImported()
        {
            var json = MigrationExport.ToBitwardenJson([_totp]);
            var converter = new BitwardenBackupConverter(CreateIconResolver());

            var result = await converter.ConvertAsync(Encoding.UTF8.GetBytes(json));

            Assert.Empty(result.Failures);
            var auth = Assert.Single(result.Backup.Authenticators);
            Assert.Equal(AuthenticatorType.Totp, auth.Type);
            Assert.Equal(_totp.Issuer, auth.Issuer);
            Assert.Equal(_totp.Username, auth.Username);
            Assert.Equal(_totp.Secret, auth.Secret);
        }

        [Fact]
        public void BitwardenCsv_QuotesFields()
        {
            var csv = MigrationExport.ToBitwardenCsv([_totp]);
            var lines = csv.Split("\r\n", System.StringSplitOptions.RemoveEmptyEntries);

            Assert.Equal(2, lines.Length);
            Assert.Equal(
                "folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp",
                lines[0]);
            Assert.Contains("\"Example, Inc.\"", lines[1]);
            Assert.Contains("\"user\"\"name\"", lines[1]);
            Assert.Contains("otpauth://totp/", lines[1]);
        }
    }
}
