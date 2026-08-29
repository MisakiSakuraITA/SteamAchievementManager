using System;
using System.Linq;
using System.Threading.Tasks;
using SAM.UI;
using Xunit;

namespace SAM.Tests
{
    [Collection(WpfCollection.Name)]
    public class CrashGuardTests
    {
        private readonly WpfTestFixture _Fixture;

        public CrashGuardTests(WpfTestFixture fixture)
        {
            this._Fixture = fixture;
        }

        [Fact]
        public void InstallingTwiceOnTheSameApplicationIsHarmless()
        {
            this._Fixture.Invoke(() =>
            {
                var app = System.Windows.Application.Current;
                var exception = Record.Exception(() =>
                {
                    CrashGuard.Install(app, "Test");
                    CrashGuard.Install(app, "Test");
                });

                Assert.Null(exception);
            });
        }

        [Fact]
        public async Task UnobservedTaskFaultsAreObservedRatherThanEscalated()
        {
            // Matched by message rather than a blanket "nothing escalated" flag: this handler
            // is process-wide, and other tests running concurrently in a different collection
            // can have their own unrelated faults pass through it too.
            const string marker = "SAM.Tests.CrashGuard.unobserved";
            var escalatedOurs = false;
            EventHandler<UnobservedTaskExceptionEventArgs> watcher = (_, e) =>
            {
                if (e.Exception.InnerExceptions.Any(x => x.Message == marker) && e.Observed == false)
                {
                    escalatedOurs = true;
                }
            };

            // CrashGuard has to be installed -- and so subscribed to the event -- before this
            // test's own watcher, so CrashGuard's handler (which calls SetObserved()) runs
            // first in the invocation list and the watcher sees the outcome, not a mid-dispatch
            // snapshot.
            this._Fixture.Invoke(() => CrashGuard.Install(System.Windows.Application.Current, "Test"));
            TaskScheduler.UnobservedTaskException += watcher;
            try
            {
                _ = Task.Run(() => throw new InvalidOperationException(marker));
                await Task.Delay(60);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                await Task.Delay(60);

                Assert.False(escalatedOurs);
            }
            finally
            {
                TaskScheduler.UnobservedTaskException -= watcher;
            }
        }
    }
}
