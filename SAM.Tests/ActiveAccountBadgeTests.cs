using System;
using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;
using SAM.UI.Controls;
using Xunit;

namespace SAM.Tests
{
    /// <summary>
    /// Drives <see cref="ActiveAccountBadge"/>'s avatar loading against real, temporary image
    /// files on the WPF fixture's own thread, since the loading itself hops to a background
    /// thread and back -- exactly the behaviour under test.
    /// </summary>
    [Collection(WpfCollection.Name)]
    public class ActiveAccountBadgeTests
    {
        private readonly WpfTestFixture _Fixture;

        public ActiveAccountBadgeTests(WpfTestFixture fixture)
        {
            this._Fixture = fixture;
        }

        private static BitmapSource MakeBitmap(int width, int height)
        {
            var stride = width * 4;
            var pixels = new byte[stride * height];
            var bitmap = BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, stride);
            bitmap.Freeze();
            return bitmap;
        }

        private static byte[] EncodePng(BitmapSource bitmap)
        {
            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using MemoryStream stream = new();
            encoder.Save(stream);
            return stream.ToArray();
        }

        private static string WriteTempImage()
        {
            var path = Path.Combine(Path.GetTempPath(), $"sam-avatar-{Guid.NewGuid():N}.png");
            File.WriteAllBytes(path, EncodePng(MakeBitmap(8, 8)));
            return path;
        }

        /// <summary>
        /// Polls <paramref name="condition"/> on the fixture's own dispatcher thread -- both
        /// to keep every access to the badge's dependency properties on the thread that owns
        /// them, and to pump the queue so a completed background load's marshalled-back
        /// continuation actually gets to run between checks.
        /// </summary>
        private bool WaitUntil(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                var result = false;
                this._Fixture.Invoke(() =>
                {
                    WpfTestFixture.Pump();
                    result = condition();
                });

                if (result == true || DateTime.UtcNow >= deadline)
                {
                    return result;
                }

                Thread.Sleep(15);
            }
        }

        [Fact]
        public void LoadsAndExposesAValidAvatarImage()
        {
            var path = WriteTempImage();
            try
            {
                ActiveAccountBadge badge = null;
                this._Fixture.Invoke(() =>
                {
                    badge = new ActiveAccountBadge();
                    badge.AvatarFilePath = path;
                });

                var loaded = this.WaitUntil(() => badge.AvatarImageSource != null, TimeSpan.FromSeconds(5));
                Assert.True(loaded, "Avatar never finished loading.");

                this._Fixture.Invoke(() =>
                {
                    Assert.IsType<BitmapImage>(badge.AvatarImageSource);
                    Assert.Equal(64, ((BitmapImage)badge.AvatarImageSource).DecodePixelWidth);
                });
            }
            finally
            {
                if (File.Exists(path) == true)
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void FallsBackToNoImageWhenTheFileDoesNotExist()
        {
            var missingPath = Path.Combine(Path.GetTempPath(), $"sam-avatar-missing-{Guid.NewGuid():N}.png");

            ActiveAccountBadge badge = null;
            this._Fixture.Invoke(() =>
            {
                badge = new ActiveAccountBadge();
                badge.AvatarFilePath = missingPath;
            });

            // A background load has no work to do here (the file check fails immediately), so
            // a short, generous wait is enough to be confident nothing ever resolves.
            this.WaitUntil(() => false, TimeSpan.FromMilliseconds(200));

            this._Fixture.Invoke(() => Assert.Null(badge.AvatarImageSource));
        }

        [Fact]
        public void FallsBackToNoImageForACorruptFile()
        {
            var path = Path.Combine(Path.GetTempPath(), $"sam-avatar-corrupt-{Guid.NewGuid():N}.png");
            File.WriteAllText(path, "not an image");
            try
            {
                ActiveAccountBadge badge = null;
                this._Fixture.Invoke(() =>
                {
                    badge = new ActiveAccountBadge();
                    badge.AvatarFilePath = path;
                });

                this.WaitUntil(() => false, TimeSpan.FromMilliseconds(200));

                this._Fixture.Invoke(() => Assert.Null(badge.AvatarImageSource));
            }
            finally
            {
                if (File.Exists(path) == true)
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void ClearingThePathResetsTheImageRightAway()
        {
            var path = WriteTempImage();
            try
            {
                ActiveAccountBadge badge = null;
                this._Fixture.Invoke(() =>
                {
                    badge = new ActiveAccountBadge();
                    badge.AvatarFilePath = path;
                });

                var loaded = this.WaitUntil(() => badge.AvatarImageSource != null, TimeSpan.FromSeconds(5));
                Assert.True(loaded, "Avatar never finished loading.");

                this._Fixture.Invoke(() =>
                {
                    badge.AvatarFilePath = null;
                    Assert.Null(badge.AvatarImageSource);
                });
            }
            finally
            {
                if (File.Exists(path) == true)
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void DisplayNameAndSteamIdTextRoundTrip()
        {
            this._Fixture.Invoke(() =>
            {
                ActiveAccountBadge badge = new() { DisplayName = "Alice", SteamIdText = "76561197960287930" };

                Assert.Equal("Alice", badge.DisplayName);
                Assert.Equal("76561197960287930", badge.SteamIdText);
            });
        }
    }
}
