// Copyright (C) 2026 Stratum fork contributors
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Stratum.WearOS.WebDav
{
    /// <summary>
    /// Minimal WebDAV client for the watch. It is intentionally independent
    /// from the phone UI so the watch can list, upload and delete files.
    /// </summary>
    public sealed class WearWebDavClient : IDisposable
    {
        private static readonly HttpMethod PropFindMethod = new("PROPFIND");
        private static readonly HttpMethod MkColMethod = new("MKCOL");
        private static readonly HashSet<string> BackupExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".stratum", ".zip", ".json", ".2fas", ".csv"
        };

        private readonly HttpClient _http;
        private readonly Uri _baseUri;
        private readonly string _username;
        private readonly string _password;
        private readonly string _directory;

        public WearWebDavClient(WearWebDavConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(config.Url)) throw new InvalidOperationException("WebDAV URL is empty");

            var url = config.Url.Trim();
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            _baseUri = new Uri(url.TrimEnd('/') + "/", UriKind.Absolute);
            _username = config.Username ?? "";
            _password = config.Password ?? "";
            _directory = config.Directory ?? "Stratum";

            var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                ConnectTimeout = TimeSpan.FromSeconds(30)
            };
            if (config.TrustAllCertificates)
            {
                handler.SslOptions.RemoteCertificateValidationCallback = IgnoreCertificateError;
            }

            _http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(3),
                DefaultRequestVersion = HttpVersion.Version11,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Stratum-WearOS-WebDav/1.0");
        }

        private static bool IgnoreCertificateError(object sender, X509Certificate certificate,
            X509Chain chain, SslPolicyErrors errors)
        {
            return true;
        }

        public async Task TestAsync()
        {
            using var response = await SendAsync(PropFindMethod, _baseUri, PropFindBody(), "0");
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new InvalidOperationException("Unauthorized (401)");
            }

            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException($"Server returned {(int) response.StatusCode} {response.ReasonPhrase}");
            }
        }

        public async Task<List<WebDavEntry>> ListAsync()
        {
            using var response = await SendAsync(PropFindMethod, DirectoryUri(), PropFindBody(), "1");
            var xml = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new InvalidOperationException("Unauthorized (401)");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new List<WebDavEntry>();
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Server returned {(int) response.StatusCode} {response.ReasonPhrase}");
            }

            XDocument document;
            try
            {
                document = XDocument.Parse(xml);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException("Server did not return valid WebDAV XML", e);
            }

            XNamespace dav = "DAV:";
            var directoryUri = DirectoryUri().ToString();
            var entries = new List<WebDavEntry>();

            foreach (var element in document.Descendants(dav + "response"))
            {
                var href = element.Element(dav + "href")?.Value;
                if (string.IsNullOrWhiteSpace(href)) continue;

                var isCollection = element.Descendants(dav + "collection").Any();
                if (isCollection) continue;

                var name = element.Descendants(dav + "displayname").FirstOrDefault()?.Value;
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = Uri.UnescapeDataString(href.TrimEnd('/').Split('/').Last());
                }

                var extension = System.IO.Path.GetExtension(name);
                if (!BackupExtensions.Contains(extension))
                {
                    continue;
                }

                var uri = new Uri(_baseUri, href);
                if (Uri.Compare(uri, DirectoryUri(), UriComponents.AbsoluteUri,
                        UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    continue;
                }

                entries.Add(new WebDavEntry
                {
                    Name = name,
                    Uri = uri,
                    Size = ParseLong(element.Descendants(dav + "getcontentlength").FirstOrDefault()?.Value),
                    LastModified = ParseDate(element.Descendants(dav + "getlastmodified").FirstOrDefault()?.Value)
                });
            }

            return entries
                .OrderByDescending(e => e.LastModified ?? DateTimeOffset.MinValue)
                .ThenByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task EnsureDirectoryAsync()
        {
            var segments = _directory.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var current = _baseUri;

            foreach (var segment in segments)
            {
                current = new Uri(current, Uri.EscapeDataString(segment) + "/");
                using var response = await SendAsync(MkColMethod, current, null, null);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new InvalidOperationException("Unauthorized (401)");
                }

                if (!response.IsSuccessStatusCode
                    && response.StatusCode != HttpStatusCode.MethodNotAllowed
                    && response.StatusCode != HttpStatusCode.Conflict)
                {
                    using var check = await SendAsync(PropFindMethod, current, PropFindBody(), "0");
                    if (check.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        throw new InvalidOperationException("Unauthorized (401)");
                    }

                    if (!check.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(
                            $"Cannot create directory {segment}: {(int) response.StatusCode} {response.ReasonPhrase}");
                    }
                }
            }
        }

        public async Task UploadAsync(string name, byte[] data)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            if (data == null) throw new ArgumentNullException(nameof(data));

            await EnsureDirectoryAsync();

            var uri = new Uri(DirectoryUri(), Uri.EscapeDataString(name));
            using var content = new ByteArrayContent(data);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var response = await SendAsync(HttpMethod.Put, uri, content, null);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new InvalidOperationException("Unauthorized (401)");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Upload failed: {(int) response.StatusCode} {response.ReasonPhrase}");
            }
        }

        public async Task<byte[]> DownloadAsync(WebDavEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            using var response = await SendAsync(HttpMethod.Get, entry.Uri, null, null);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new InvalidOperationException("Unauthorized (401)");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Download failed: {(int) response.StatusCode} {response.ReasonPhrase}");
            }

            return await response.Content.ReadAsByteArrayAsync();
        }

        public async Task DeleteAsync(WebDavEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            using var response = await SendAsync(HttpMethod.Delete, entry.Uri, null, null);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new InvalidOperationException("Unauthorized (401)");
            }

            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException($"Delete failed: {(int) response.StatusCode} {response.ReasonPhrase}");
            }
        }

        private Uri DirectoryUri()
        {
            var segments = _directory.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return new Uri(_baseUri, string.Join("/", segments.Select(Uri.EscapeDataString)) + "/");
        }

        private static ByteArrayContent PropFindBody()
        {
            const string body = "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
                                + "<d:propfind xmlns:d=\"DAV:\"><d:prop>"
                                + "<d:resourcetype/><d:displayname/><d:getcontentlength/><d:getlastmodified/>"
                                + "</d:prop></d:propfind>";
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/xml") { CharSet = "utf-8" };
            return content;
        }

        private async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri uri, HttpContent content,
            string depth)
        {
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_username}:{_password}")));

            if (depth != null)
            {
                request.Headers.TryAddWithoutValidation("Depth", depth);
            }

            if (content != null)
            {
                request.Content = content;
            }

            return await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead);
        }

        private static long ParseLong(string value)
        {
            return long.TryParse(value, out var result) ? result : 0L;
        }

        private static DateTimeOffset? ParseDate(string value)
        {
            return DateTimeOffset.TryParse(value, out var result) ? result : null;
        }

        public void Dispose()
        {
            _http?.Dispose();
        }
    }

    public sealed class WebDavEntry
    {
        public string Name { get; set; }
        public Uri Uri { get; set; }
        public long Size { get; set; }
        public DateTimeOffset? LastModified { get; set; }

        public string DisplaySize
        {
            get
            {
                if (Size <= 0) return "";
                if (Size < 1024) return $"{Size} B";
                if (Size < 1024 * 1024) return $"{Size / 1024.0:F1} KB";
                return $"{Size / 1024.0 / 1024.0:F1} MB";
            }
        }
    }
}
