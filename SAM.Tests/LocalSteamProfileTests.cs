using System;
using System.IO;
using SAM.Core.Steam;
using Xunit;

namespace SAM.Tests
{
    /// <summary>
    /// Exercises <see cref="LocalSteamProfile.GetPersonaName"/> and
    /// <see cref="LocalSteamProfile.GetAvatarFilePath"/> against a real, temporary
    /// <c>config/loginusers.vdf</c> (and, for the avatar, real files under
    /// <c>config/avatars</c>), since those file reads are exactly the behaviour under test.
    /// </summary>
    public class LocalSteamProfileTests
    {
        private sealed class TempInstall : IDisposable
        {
            public readonly string Path;

            public TempInstall()
            {
                this.Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sam-install-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(System.IO.Path.Combine(this.Path, "config"));
            }

            public void WriteLoginUsers(string content)
            {
                File.WriteAllText(System.IO.Path.Combine(this.Path, "config", "loginusers.vdf"), content);
            }

            /// <summary>Writes a tiny placeholder file under config/avatars/, creating the directory as needed.</summary>
            public string WriteAvatarFile(string fileName)
            {
                var avatarsDirectory = System.IO.Path.Combine(this.Path, "config", "avatars");
                Directory.CreateDirectory(avatarsDirectory);
                var fullPath = System.IO.Path.Combine(avatarsDirectory, fileName);
                File.WriteAllBytes(fullPath, new byte[] { 1, 2, 3 });
                return fullPath;
            }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(this.Path, true);
                }
                catch (Exception)
                {
                    // Best-effort cleanup; a locked handle here should not fail the test run.
                }
            }
        }

        private const string _Sample = @"
""users""
{
	""76561197960287930""
	{
		""AccountName""		""exampleaccount""
		""PersonaName""		""Example Person""
		""avatar""		""abc123hash""
		""MostRecent""		""1""
	}
}
";

        [Fact]
        public void ReturnsThePersonaNameForAKnownSteamId()
        {
            using var install = new TempInstall();
            install.WriteLoginUsers(_Sample);

            var name = LocalSteamProfile.GetPersonaName(install.Path, 76561197960287930UL);

            Assert.Equal("Example Person", name);
        }

        [Fact]
        public void ReturnsNullForASteamIdTheFileDoesNotMention()
        {
            using var install = new TempInstall();
            install.WriteLoginUsers(_Sample);

            var name = LocalSteamProfile.GetPersonaName(install.Path, 1UL);

            Assert.Null(name);
        }

        [Fact]
        public void ReturnsNullWhenTheFileDoesNotExist()
        {
            using var install = new TempInstall();
            // Deliberately not writing loginusers.vdf at all.

            var name = LocalSteamProfile.GetPersonaName(install.Path, 76561197960287930UL);

            Assert.Null(name);
        }

        [Fact]
        public void ReturnsNullForAMissingInstallPath()
        {
            Assert.Null(LocalSteamProfile.GetPersonaName(null, 76561197960287930UL));
            Assert.Null(LocalSteamProfile.GetPersonaName("", 76561197960287930UL));
        }

        [Fact]
        public void ReturnsNullForASteamIdOfZero()
        {
            using var install = new TempInstall();
            install.WriteLoginUsers(_Sample);

            Assert.Null(LocalSteamProfile.GetPersonaName(install.Path, 0UL));
        }

        [Fact]
        public void ReturnsNullRatherThanThrowingForACorruptFile()
        {
            using var install = new TempInstall();
            install.WriteLoginUsers("\"users\"\n{\n\t\"orphaned-key\"\n}\n");

            var name = LocalSteamProfile.GetPersonaName(install.Path, 76561197960287930UL);

            Assert.Null(name);
        }

        // ============================ avatar ============================

        [Fact]
        public void ResolvesTheFullSuffixedJpgWhenItExists()
        {
            using var install = new TempInstall();
            install.WriteLoginUsers(_Sample);
            var expected = install.WriteAvatarFile("abc123hash_full.jpg");

            var path = LocalSteamProfile.GetAvatarFilePath(install.Path, 76561197960287930UL);

            Assert.Equal(expected, path);
        }

        [Fact]
        public void FallsBackToThePlainJpgWhenTheFullSuffixedOneIsMissing()
        {
            using var install = new TempInstall();
            install.WriteLoginUsers(_Sample);
            var expected = install.WriteAvatarFile("abc123hash.jpg");

            var path = LocalSteamProfile.GetAvatarFilePath(install.Path, 76561197960287930UL);

            Assert.Equal(expected, path);
        }

        [Fact]
        public void FallsBackToThePngWhenNeitherJpgVariantExists()
        {
            using var install = new TempInstall();
            install.WriteLoginUsers(_Sample);
            var expected = install.WriteAvatarFile("abc123hash.png");

            var path = LocalSteamProfile.GetAvatarFilePath(install.Path, 76561197960287930UL);

            Assert.Equal(expected, path);
        }

        [Fact]
        public void PrefersTheFullSuffixedJpgOverThePlainOneWhenBothExist()
        {
            using var install = new TempInstall();
            install.WriteLoginUsers(_Sample);
            var preferred = install.WriteAvatarFile("abc123hash_full.jpg");
            install.WriteAvatarFile("abc123hash.jpg");
            install.WriteAvatarFile("abc123hash.png");

            var path = LocalSteamProfile.GetAvatarFilePath(install.Path, 76561197960287930UL);

            Assert.Equal(preferred, path);
        }

        [Fact]
        public void ReturnsNullWhenTheHashIsKnownButNoCachedFileExists()
        {
            using var install = new TempInstall();
            install.WriteLoginUsers(_Sample);
            // Deliberately not writing any file under config/avatars/.

            var path = LocalSteamProfile.GetAvatarFilePath(install.Path, 76561197960287930UL);

            Assert.Null(path);
        }

        [Fact]
        public void ReturnsNullWhenTheAccountHasNoAvatarHashRecorded()
        {
            const string sampleWithoutAvatar = @"
""users""
{
	""76561197960287930""
	{
		""PersonaName""		""No Avatar Here""
	}
}
";
            using var install = new TempInstall();
            install.WriteLoginUsers(sampleWithoutAvatar);
            install.WriteAvatarFile("abc123hash_full.jpg");

            var path = LocalSteamProfile.GetAvatarFilePath(install.Path, 76561197960287930UL);

            Assert.Null(path);
        }

        [Fact]
        public void AvatarLookupReturnsNullForAMissingInstallPathOrZeroSteamId()
        {
            using var install = new TempInstall();
            install.WriteLoginUsers(_Sample);
            install.WriteAvatarFile("abc123hash_full.jpg");

            Assert.Null(LocalSteamProfile.GetAvatarFilePath(null, 76561197960287930UL));
            Assert.Null(LocalSteamProfile.GetAvatarFilePath("", 76561197960287930UL));
            Assert.Null(LocalSteamProfile.GetAvatarFilePath(install.Path, 0UL));
        }

        [Fact]
        public void AvatarLookupReturnsNullRatherThanThrowingForACorruptFile()
        {
            using var install = new TempInstall();
            install.WriteLoginUsers("\"users\"\n{\n\t\"orphaned-key\"\n}\n");

            var path = LocalSteamProfile.GetAvatarFilePath(install.Path, 76561197960287930UL);

            Assert.Null(path);
        }
    }
}
