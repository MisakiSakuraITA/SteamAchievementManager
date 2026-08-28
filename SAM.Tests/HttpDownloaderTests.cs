using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SAM.Core.Net;
using Xunit;

namespace SAM.Tests
{
    /// <summary>
    /// Exercises TryGetBytesAsync's transient/permanent classification against a minimal,
    /// hand-rolled HTTP responder on a loopback socket.
    /// </summary>
    /// <remarks>
    /// A raw <see cref="TcpListener"/> rather than <see cref="HttpListener"/>: the latter needs
    /// a URL ACL reservation or elevation for its prefix on Windows, which a plain loopback TCP
    /// socket on an OS-assigned port does not.
    /// </remarks>
    public class HttpDownloaderTests
    {
        private static Uri StartOneShotServer(int statusCode, string reason, string body = "")
        {
            TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            _ = Task.Run(async () =>
            {
                try
                {
                    using var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    using var stream = client.GetStream();

                    var buffer = new byte[8192];
                    var received = "";
                    while (received.Contains("\r\n\r\n") == false)
                    {
                        var read = await stream.ReadAsync(buffer).ConfigureAwait(false);
                        if (read <= 0)
                        {
                            return;
                        }
                        received += Encoding.ASCII.GetString(buffer, 0, read);
                    }

                    var bodyBytes = Encoding.UTF8.GetBytes(body ?? "");
                    var header = $"HTTP/1.1 {statusCode} {reason}\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(header)).ConfigureAwait(false);
                    await stream.WriteAsync(bodyBytes).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The test asserts on TryGetBytesAsync's own outcome; a torn-down
                    // connection here has nothing left to report to.
                }
                finally
                {
                    listener.Stop();
                }
            });

            return new Uri($"http://127.0.0.1:{port}/");
        }

        [Fact]
        public async Task NotFoundIsClassifiedAsPermanent()
        {
            var uri = StartOneShotServer(404, "Not Found");

            var result = await HttpDownloader.TryGetBytesAsync(uri, CancellationToken.None);

            Assert.Null(result.Data);
            Assert.False(result.IsTransientFailure);
        }

        [Fact]
        public async Task ServerErrorIsClassifiedAsTransient()
        {
            var uri = StartOneShotServer(503, "Service Unavailable");

            var result = await HttpDownloader.TryGetBytesAsync(uri, CancellationToken.None);

            Assert.Null(result.Data);
            Assert.True(result.IsTransientFailure);
        }

        [Fact]
        public async Task SuccessReturnsTheBodyAndIsNotAFailureOfEitherKind()
        {
            const string payload = "hello from the loopback server";
            var uri = StartOneShotServer(200, "OK", payload);

            var result = await HttpDownloader.TryGetBytesAsync(uri, CancellationToken.None);

            Assert.NotNull(result.Data);
            Assert.Equal(payload, Encoding.UTF8.GetString(result.Data));
            Assert.False(result.IsTransientFailure);
        }

        [Fact]
        public async Task GenuineCancellationPropagatesRatherThanBeingSwallowedAsTransient()
        {
            using CancellationTokenSource cts = new();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => HttpDownloader.TryGetBytesAsync(new Uri("http://127.0.0.1:1/unreachable"), cts.Token));
        }

        [Fact]
        public async Task NullUriIsPermanentWithoutAttemptingAnyRequest()
        {
            var result = await HttpDownloader.TryGetBytesAsync(null, CancellationToken.None);

            Assert.Null(result.Data);
            Assert.False(result.IsTransientFailure);
        }
    }
}
