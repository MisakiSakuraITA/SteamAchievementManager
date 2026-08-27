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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SAM.API
{
    public class Client : IDisposable
    {
        public Wrappers.SteamClient018 SteamClient;
        public Wrappers.SteamUser012 SteamUser;
        public Wrappers.SteamUserStats013 SteamUserStats;
        public Wrappers.SteamUtils005 SteamUtils;
        public Wrappers.SteamApps001 SteamApps001;
        public Wrappers.SteamApps008 SteamApps008;

        private bool _IsDisposed = false;
        private int _Pipe;
        private int _User;

        private readonly List<ICallback> _Callbacks = new();

        public void Initialize(long appId)
        {
            if (string.IsNullOrEmpty(Steam.GetInstallPath()) == true)
            {
                throw new ClientInitializeException(ClientInitializeFailure.GetInstallPath, "failed to get Steam install path");
            }

            if (appId != 0)
            {
                Environment.SetEnvironmentVariable("SteamAppId", appId.ToString(CultureInfo.InvariantCulture));
            }

            if (Steam.Load() == false)
            {
                throw new ClientInitializeException(ClientInitializeFailure.Load, "failed to load SteamClient");
            }

            this.SteamClient = Steam.CreateInterface<Wrappers.SteamClient018>("SteamClient018");
            if (this.SteamClient == null)
            {
                throw new ClientInitializeException(ClientInitializeFailure.CreateSteamClient, "failed to create ISteamClient018");
            }

            this._Pipe = this.SteamClient.CreateSteamPipe();
            if (this._Pipe == 0)
            {
                throw new ClientInitializeException(ClientInitializeFailure.CreateSteamPipe, "failed to create pipe");
            }

            this._User = this.SteamClient.ConnectToGlobalUser(this._Pipe);
            if (this._User == 0)
            {
                throw new ClientInitializeException(ClientInitializeFailure.ConnectToGlobalUser, "failed to connect to global user");
            }

            this.SteamUtils = this.SteamClient.GetSteamUtils004(this._Pipe);
            if (appId > 0 && this.SteamUtils.GetAppId() != (uint)appId)
            {
                throw new ClientInitializeException(ClientInitializeFailure.AppIdMismatch, "appID mismatch");
            }

            this.SteamUser = this.SteamClient.GetSteamUser012(this._User, this._Pipe);
            this.SteamUserStats = this.SteamClient.GetSteamUserStats013(this._User, this._Pipe);
            this.SteamApps001 = this.SteamClient.GetSteamApps001(this._User, this._Pipe);
            this.SteamApps008 = this.SteamClient.GetSteamApps008(this._User, this._Pipe);
        }

        /// <summary>
        /// Raised once, on the thread running the callback pump, when the Steam pipe stops
        /// answering. Steam has to be restarted before this client is usable again.
        /// </summary>
        public event Action Disconnected;

        /// <summary>
        /// Raised when a callback subscriber threw. The pump keeps running regardless; this
        /// exists so the shell can surface a fault that would otherwise be invisible.
        /// </summary>
        public event Action<Exception> CallbackFaulted;

        /// <summary>Whether the Steam pipe was still answering as of the last pump.</summary>
        public bool IsConnected => this._IsConnected;

        ~Client()
        {
            this.Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (this._IsDisposed == true)
            {
                return;
            }

            this._IsDisposed = true;

            // Everything below reaches into steamclient.dll, and the pipe belongs to the
            // thread that opened it. On the finalizer path there is no safe way to honour
            // that affinity, so the pipe is deliberately left for process teardown to
            // reclaim rather than released from the wrong thread.
            if (disposing == false)
            {
                return;
            }

            this.UnregisterAllCallbacks();

            if (this.SteamClient != null && this._Pipe > 0)
            {
                if (this._User > 0)
                {
                    this.SteamClient.ReleaseUser(this._Pipe, this._User);
                    this._User = 0;
                }

                this.SteamClient.ReleaseSteamPipe(this._Pipe);
                this._Pipe = 0;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public TCallback CreateAndRegisterCallback<TCallback>()
            where TCallback : ICallback, new()
        {
            TCallback callback = new();
            lock (this._CallbackLock)
            {
                this._Callbacks.Add(callback);
            }
            return callback;
        }

        /// <summary>
        /// Stops dispatching to a callback. Safe to call while the pump is running, and safe
        /// to call more than once.
        /// </summary>
        public void UnregisterCallback(ICallback callback)
        {
            if (callback == null)
            {
                return;
            }

            lock (this._CallbackLock)
            {
                this._Callbacks.Remove(callback);
            }
        }

        public void UnregisterAllCallbacks()
        {
            lock (this._CallbackLock)
            {
                this._Callbacks.Clear();
            }
        }

        private readonly object _CallbackLock = new();

        private bool _RunningCallbacks;
        private bool _IsConnected = true;

        public void RunCallbacks(bool server)
        {
            if (this._IsDisposed == true || this._RunningCallbacks == true)
            {
                return;
            }

            this._RunningCallbacks = true;
            try
            {
                Types.CallbackMessage message;
                while (Steam.GetCallback(this._Pipe, out message, out _) == true)
                {
                    try
                    {
                        this.Dispatch(message, server);
                    }
                    finally
                    {
                        // The native queue entry has to be released whatever the subscribers
                        // did, or the pipe backs up behind an entry that is never freed.
                        Steam.FreeLastCallback(this._Pipe);
                    }
                }
            }
            finally
            {
                // Guaranteed, so one throw can never wedge the pump into a state where every
                // later tick returns at the re-entrancy guard and Steam goes quiet forever.
                this._RunningCallbacks = false;
            }

            this.CheckConnection();
        }

        private void Dispatch(Types.CallbackMessage message, bool server)
        {
            var callbackId = message.Id;

            // Snapshot: a subscriber is allowed to register or unregister callbacks while it
            // is being dispatched to, which would otherwise invalidate the enumerator.
            ICallback[] targets;
            lock (this._CallbackLock)
            {
                targets = this._Callbacks
                    .Where(candidate => candidate.Id == callbackId && candidate.IsServer == server)
                    .ToArray();
            }

            foreach (var callback in targets)
            {
                try
                {
                    callback.Run(message.ParamPointer);
                }
                catch (Exception e)
                {
                    // One bad subscriber must not cost the others their callback, leak the
                    // native queue entry, or take down the process.
                    this.RaiseCallbackFaulted(e);
                }
            }
        }

        private void RaiseCallbackFaulted(Exception exception)
        {
            try
            {
                this.CallbackFaulted?.Invoke(exception);
            }
            catch (Exception)
            {
                // The fault reporter itself is not allowed to break the pump.
            }
        }

        /// <summary>
        /// Asks Steam which universe the pipe is connected to. A dead pipe reports the invalid
        /// universe, which is the cheapest reliable signal that the client has gone away.
        /// </summary>
        private void CheckConnection()
        {
            if (this._IsConnected == false || this._IsDisposed == true)
            {
                return;
            }

            bool alive;
            try
            {
                alive = this.SteamUtils != null && this.SteamUtils.GetConnectedUniverse() != 0;
            }
            catch (Exception)
            {
                alive = false;
            }

            if (alive == true)
            {
                return;
            }

            this._IsConnected = false;

            try
            {
                this.Disconnected?.Invoke();
            }
            catch (Exception)
            {
                // Reporting the disconnect must not itself throw out of the timer tick.
            }
        }
    }
}
