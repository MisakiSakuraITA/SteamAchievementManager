/* Copyright (c) 2024 Rick (rick 'at' gibbed 'dot' us)
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SAM.Core.Net
{
    /// <summary>
    /// The single <see cref="HttpClient"/> used by the whole application.
    /// </summary>
    /// <remarks>
    /// One long-lived client is deliberate: a client per request leaks sockets in TIME_WAIT
    /// and re-pays connection setup on every icon. The instance is created once and disposed
    /// once, on the way out of <c>Main</c>.
    /// </remarks>
    public static class HttpDownloader
    {
        private const int _MaximumConnectionsPerServer = 16;
        private const string _UserAgent = "SteamAchievementManager/7.0";

        private static readonly TimeSpan _RequestTimeout = TimeSpan.FromSeconds(30);
        private static readonly object _Lock = new();

        private static HttpClient _Client;

        public static HttpClient Client
        {
            get
            {
                lock (_Lock)
                {
                    return _Client ??= Create();
                }
            }
        }

        private static HttpClient Create()
        {
            // On .NET Framework HttpClient is layered over ServicePointManager, whose default
            // of two connections per endpoint serialises icon fetches no matter how much
            // concurrency the caller asks for.
            if (ServicePointManager.DefaultConnectionLimit < _MaximumConnectionsPerServer)
            {
                ServicePointManager.DefaultConnectionLimit = _MaximumConnectionsPerServer;
            }

            HttpClientHandler handler = new()
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                MaxConnectionsPerServer = _MaximumConnectionsPerServer,
            };

            HttpClient client = new(handler, disposeHandler: true)
            {
                Timeout = _RequestTimeout,
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(_UserAgent);
            return client;
        }

        /// <summary>
        /// Downloads a resource. <see cref="DownloadResult.Data"/> is <see langword="null"/>
        /// for anything that is not a usable payload, with
        /// <see cref="DownloadResult.IsTransientFailure"/> saying whether the failure is worth
        /// trying again later (a timeout, a DNS or transport failure, a server error) or is
        /// as good as permanent (a 404: retrying will not make the resource exist).
        /// </summary>
        public static async Task<DownloadResult> TryGetBytesAsync(Uri uri, CancellationToken cancellationToken)
        {
            if (uri == null)
            {
                return DownloadResult.Permanent;
            }

            try
            {
                using (HttpRequestMessage request = new(HttpMethod.Get, uri))
                using (var response = await Client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false))
                {
                    if (response.IsSuccessStatusCode == false)
                    {
                        // A 404 means the resource does not exist; nothing about trying again
                        // changes that. Anything else -- rate limiting, a server-side error --
                        // is the server's problem this moment, not a verdict on the asset.
                        return response.StatusCode == HttpStatusCode.NotFound
                            ? DownloadResult.Permanent
                            : DownloadResult.Transient;
                    }

                    var data = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    return new DownloadResult(data, isTransientFailure: false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == true)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // HttpClient surfaces its own timeout as a cancellation.
                return DownloadResult.Transient;
            }
            catch (HttpRequestException)
            {
                return DownloadResult.Transient;
            }
            catch (IOException)
            {
                return DownloadResult.Transient;
            }
            catch (ObjectDisposedException)
            {
                // Shutdown raced with an in-flight request -- not a real signal either way,
                // but "transient" is the safe side to default to.
                return DownloadResult.Transient;
            }
        }

        public readonly struct DownloadResult
        {
            public static readonly DownloadResult Permanent = new(null, isTransientFailure: false);
            public static readonly DownloadResult Transient = new(null, isTransientFailure: true);

            public readonly byte[] Data;
            public readonly bool IsTransientFailure;

            public DownloadResult(byte[] data, bool isTransientFailure)
            {
                this.Data = data;
                this.IsTransientFailure = isTransientFailure;
            }
        }

        /// <summary>
        /// Releases the shared client. Call once, after the message loop has ended.
        /// </summary>
        public static void Shutdown()
        {
            HttpClient client;
            lock (_Lock)
            {
                client = _Client;
                _Client = null;
            }
            client?.Dispose();
        }
    }
}
