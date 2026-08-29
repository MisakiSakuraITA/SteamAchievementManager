using System;
using System.IO;
using SAM.Core.Steam;
using Xunit;

namespace SAM.Tests
{
    /// <summary>
    /// Exercises <see cref="LocalSteamProfile.GetPersonaName"/> against a real, temporary
    /// <c>config/loginusers.vdf</c>, since that file read is exactly the behaviour under test.
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
    }
}
