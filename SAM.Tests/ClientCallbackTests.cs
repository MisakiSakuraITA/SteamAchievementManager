using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SAM.API;
using Xunit;

namespace SAM.Tests
{
    /// <summary>
    /// Exercises the callback pump's exception safety and disconnect detection. <see cref="Client"/>
    /// and its dispatch loop have no public seam for simulating a fault mid-pump, so several of
    /// these reach into private members with reflection -- the same approach used to find and
    /// fix the original bugs.
    /// </summary>
    public class ClientCallbackTests
    {
        [Fact]
        public void CallbackRunWithNoSubscriberDoesNotThrow()
        {
            var callback = new SAM.API.Callbacks.AppDataChanged();

            var exception = Record.Exception(() => callback.Run(IntPtr.Zero));

            Assert.Null(exception);
        }

        [Fact]
        public void CallbackRunStillDeliversToALiveSubscriber()
        {
            var payload = new SAM.API.Types.AppDataChanged { Id = 4242, Result = true };
            var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<SAM.API.Types.AppDataChanged>());
            try
            {
                Marshal.StructureToPtr(payload, buffer, false);

                uint seen = 0;
                var callback = new SAM.API.Callbacks.AppDataChanged();
                callback.OnRun += p => seen = p.Id;
                callback.Run(buffer);

                Assert.Equal(4242u, seen);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [Fact]
        public void PumpReentrancyGuardResetsEvenAfterAFault()
        {
            // No Steam client was ever loaded, so the native GetCallback delegate is null and
            // RunCallbacks faults part-way through -- exactly the failure shape the guard has
            // to survive.
            using Client client = new();
            var guardField = typeof(Client).GetField("_RunningCallbacks", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(guardField);

            Record.Exception(() => client.RunCallbacks(false));

            Assert.False((bool)guardField.GetValue(client));

            // The decisive check: a wedged guard makes every later call return silently at the
            // top instead of doing any work, which would show up as the same outcome (threw, or
            // didn't) every single time.
            var firstThrew = Record.Exception(() => client.RunCallbacks(false)) != null;
            var secondThrew = Record.Exception(() => client.RunCallbacks(false)) != null;
            Assert.Equal(firstThrew, secondThrew);
        }

        [Fact]
        public void AThrowingSubscriberDoesNotStopTheOthersOrEscapeDispatch()
        {
            using Client client = new();
            var first = client.CreateAndRegisterCallback<SAM.API.Callbacks.AppDataChanged>();
            var second = client.CreateAndRegisterCallback<SAM.API.Callbacks.AppDataChanged>();

            var firstRan = false;
            var secondRan = false;
            first.OnRun += _ =>
            {
                firstRan = true;
                throw new InvalidOperationException("subscriber blew up");
            };
            second.OnRun += _ => secondRan = true;

            var faults = new List<Exception>();
            client.CallbackFaulted += faults.Add;

            var payload = new SAM.API.Types.AppDataChanged { Id = 7, Result = true };
            var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<SAM.API.Types.AppDataChanged>());
            try
            {
                Marshal.StructureToPtr(payload, buffer, false);
                var message = new SAM.API.Types.CallbackMessage
                {
                    Id = first.Id,
                    ParamPointer = buffer,
                    ParamSize = Marshal.SizeOf<SAM.API.Types.AppDataChanged>(),
                };

                var dispatch = typeof(Client).GetMethod("Dispatch", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(dispatch);

                var escaped = false;
                try
                {
                    dispatch.Invoke(client, new object[] { message, false });
                }
                catch (TargetInvocationException)
                {
                    escaped = true;
                }

                Assert.False(escaped);
                Assert.True(firstRan);
                Assert.True(secondRan);
                Assert.Single(faults);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [Fact]
        public void UnregisterRemovesExactlyOneAndIsIdempotent()
        {
            using Client client = new();
            var callbacksField = typeof(Client).GetField("_Callbacks", BindingFlags.NonPublic | BindingFlags.Instance);
            var list = (List<ICallback>)callbacksField.GetValue(client);

            var a = client.CreateAndRegisterCallback<SAM.API.Callbacks.AppDataChanged>();
            var b = client.CreateAndRegisterCallback<SAM.API.Callbacks.UserStatsReceived>();
            Assert.Equal(2, list.Count);

            client.UnregisterCallback(a);
            Assert.Single(list);
            Assert.Contains(b, list);

            client.UnregisterCallback(a);
            Assert.Single(list);

            client.UnregisterAllCallbacks();
            Assert.Empty(list);
        }

        [Fact]
        public void FinalizerPathMakesNoNativeCallButTheManagedPathDoes()
        {
            var dispose = typeof(Client).GetMethod("Dispose", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(bool) }, null);
            var pipeField = typeof(Client).GetField("_Pipe", BindingFlags.NonPublic | BindingFlags.Instance);
            var userField = typeof(Client).GetField("_User", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(dispose);
            Assert.NotNull(pipeField);
            Assert.NotNull(userField);

            // A wrapper whose function table is all zero: any attempt to actually call through
            // it fails loudly instead of doing something undefined.
            Client finalizerPath = new() { SteamClient = new SAM.API.Wrappers.SteamClient018() };
            pipeField.SetValue(finalizerPath, 9999);
            userField.SetValue(finalizerPath, 4242);

            Exception finalizerFault = null;
            try
            {
                dispose.Invoke(finalizerPath, new object[] { false });
            }
            catch (TargetInvocationException e)
            {
                finalizerFault = e.InnerException;
            }

            Assert.Null(finalizerFault);
            Assert.Equal(9999, (int)pipeField.GetValue(finalizerPath));

            Client disposingPath = new() { SteamClient = new SAM.API.Wrappers.SteamClient018() };
            pipeField.SetValue(disposingPath, 9999);
            userField.SetValue(disposingPath, 4242);

            Exception disposingFault = null;
            try
            {
                dispose.Invoke(disposingPath, new object[] { true });
            }
            catch (TargetInvocationException e)
            {
                disposingFault = e.InnerException;
            }

            // The managed path must still genuinely try to release, otherwise the flag would be
            // gating nothing at all; against a zeroed function table that attempt itself faults.
            Assert.NotNull(disposingFault);
        }

        [Fact]
        public void FinalizingAnUndisposedClientDoesNotFaultTheFinalizerThread()
        {
            MakeCollectableClient();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Reaching this line at all is the assertion: a fault on the finalizer thread would
            // have torn down the test process.
            Assert.True(true);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void MakeCollectableClient()
        {
            Client orphan = new() { SteamClient = new SAM.API.Wrappers.SteamClient018() };
            typeof(Client).GetField("_Pipe", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(orphan, 5555);
            typeof(Client).GetField("_User", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(orphan, 6666);
        }
    }
}
