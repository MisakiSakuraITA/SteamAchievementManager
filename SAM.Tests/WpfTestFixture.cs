using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Xunit;

namespace SAM.Tests
{
    /// <summary>
    /// Hosts one long-lived STA thread with a pumping <see cref="Dispatcher"/> and a WPF
    /// <see cref="Application"/> whose resources include the shell's dark theme, for the
    /// tests that need to construct real windows, controls or imaging types.
    /// </summary>
    /// <remarks>
    /// WPF's <see cref="Application"/> can only ever be constructed once per process and is
    /// bound to the thread that created it, so every test that needs one shares this single
    /// fixture (via <see cref="WpfCollection"/>) rather than each spinning up its own.
    /// <see cref="Invoke"/> marshals a test body onto that thread and re-throws whatever it
    /// threw, so a failing assertion still fails the test in the usual way.
    /// </remarks>
    public sealed class WpfTestFixture : IDisposable
    {
        private readonly Thread _Thread;
        private readonly Dispatcher _Dispatcher;

        public WpfTestFixture()
        {
            using var ready = new ManualResetEventSlim(false);
            Dispatcher dispatcher = null;

            this._Thread = new Thread(() =>
            {
                dispatcher = Dispatcher.CurrentDispatcher;

                Application app = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/SAM.UI;component/Theme/DarkTheme.xaml", UriKind.Absolute),
                });

                ready.Set();
                Dispatcher.Run();
            })
            {
                IsBackground = true,
            };
            this._Thread.SetApartmentState(ApartmentState.STA);
            this._Thread.Start();
            ready.Wait();
            this._Dispatcher = dispatcher;
        }

        /// <summary>
        /// Runs <paramref name="action"/> on the fixture's STA thread and waits for it.
        /// <see cref="Dispatcher.Invoke(Action)"/> already propagates whatever the callback
        /// throws back to this call, so a failing assertion inside it still fails the test.
        /// </summary>
        public void Invoke(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            this._Dispatcher.Invoke(action);
        }

        /// <summary>
        /// Pumps the dispatcher queue down to <see cref="DispatcherPriority.ContextIdle"/>,
        /// exactly like the real callback timer's tick does, so layout and any queued
        /// continuations settle before the next assertion.
        /// </summary>
        public static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        public void Dispose()
        {
            this._Dispatcher.InvokeShutdown();
            this._Thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    [CollectionDefinition(Name)]
    public sealed class WpfCollection : ICollectionFixture<WpfTestFixture>
    {
        public const string Name = "WPF";
    }
}
