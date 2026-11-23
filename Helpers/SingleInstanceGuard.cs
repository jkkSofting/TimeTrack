// Target: .NET Framework + C# 7.3
using System;
using System.Diagnostics;
using System.Threading;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// Lets at most one release instance of the app run per Windows session. Debug builds are not
    /// restricted (see <see cref="IsRestrictedBuild"/>), so any number of them can run next to it.
    ///
    /// The first instance holds a named mutex. A later instance does not get it, signals the first
    /// one to come to the front (named event) and exits. A restart started by the app itself
    /// (<see cref="RestartCurrentProcess"/>) passes <see cref="RestartArgument"/> and waits for the
    /// old process to let go of the mutex instead of giving up.
    /// </summary>
    internal sealed class SingleInstanceGuard : IDisposable
    {
        /// <summary>Command line argument of a process the app started to replace itself.</summary>
        public const string RestartArgument = "--restart";

        /// <summary>How long a restarted process waits for the old one to exit.</summary>
        public static readonly TimeSpan RestartWait = TimeSpan.FromSeconds(30);

        private const string DefaultName = "TimeTrack.Zeitmanagement.SingleInstance";

        private readonly string _mutexName;
        private readonly string _activateEventName;
        private Mutex _mutex;
        private bool _ownsMutex;
        private EventWaitHandle _activateEvent;
        private Thread _listenerThread;
        private volatile bool _disposed;

        public SingleInstanceGuard() : this(DefaultName)
        {
        }

        /// <summary>The name is injectable so tests do not collide with a running app.</summary>
        public SingleInstanceGuard(string name)
        {
            _mutexName = @"Local\" + name;
            _activateEventName = @"Local\" + name + ".Activate";
        }

        /// <summary>True for release builds: only those are limited to one instance.</summary>
        public static bool IsRestrictedBuild
        {
            get
            {
#if DEBUG
                return false;
#else
                return true;
#endif
            }
        }

        /// <summary>True if this process holds the mutex, i.e. is the one running instance.</summary>
        public bool IsPrimaryInstance => _ownsMutex;

        /// <summary>
        /// Tries to become the running instance. Waits up to <paramref name="wait"/> for an old
        /// instance to exit (use <see cref="TimeSpan.Zero"/> for no waiting). Must be called on the
        /// thread that later disposes the guard: a mutex belongs to the thread that took it.
        /// </summary>
        public bool TryAcquire(TimeSpan wait)
        {
            if (_ownsMutex)
                return true;

            _mutex = new Mutex(false, _mutexName);
            try
            {
                _ownsMutex = _mutex.WaitOne(wait);
            }
            catch (AbandonedMutexException)
            {
                // The previous instance crashed without releasing it - we own it now.
                _ownsMutex = true;
            }

            if (!_ownsMutex)
            {
                _mutex.Dispose();
                _mutex = null;
            }

            return _ownsMutex;
        }

        /// <summary>Asks the running instance (if any) to bring its window to the front.</summary>
        public bool SignalRunningInstance()
        {
            try
            {
                using (var handle = EventWaitHandle.OpenExisting(_activateEventName))
                {
                    return handle.Set();
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Starts listening for later instances. <paramref name="onActivationRequested"/> is called
        /// on a background thread every time another instance asked to come to the front.
        /// </summary>
        public void StartListening(Action onActivationRequested)
        {
            if (!_ownsMutex)
                throw new InvalidOperationException("Only the running instance can listen.");
            if (_listenerThread != null)
                return;

            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _activateEventName);
            _listenerThread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        _activateEvent.WaitOne();
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }

                    if (_disposed)
                        return;

                    try
                    {
                        onActivationRequested?.Invoke();
                    }
                    catch (Exception)
                    {
                        // A failing callback must not end the listener.
                    }
                }
            })
            {
                IsBackground = true,
                Name = "SingleInstanceListener"
            };
            _listenerThread.Start();
        }

        /// <summary>
        /// Starts a fresh copy of this app and expects the caller to shut the current one down
        /// right after. The new process waits for this one to release the mutex.
        /// </summary>
        public static void RestartCurrentProcess()
        {
            Process.Start(Process.GetCurrentProcess().MainModule.FileName, RestartArgument);
        }

        /// <summary>True if the command line says this process was started by a restart.</summary>
        public static bool IsRestart(string[] args)
        {
            if (args == null)
                return false;

            foreach (var arg in args)
            {
                if (string.Equals(arg, RestartArgument, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            // Wake the listener so it can see the flag and end.
            try { _activateEvent?.Set(); } catch (ObjectDisposedException) { }

            if (_mutex != null)
            {
                if (_ownsMutex)
                {
                    try { _mutex.ReleaseMutex(); }
                    catch (ApplicationException) { /* released from another thread: process exit frees it. */ }
                }

                _mutex.Dispose();
                _mutex = null;
            }

            _ownsMutex = false;
            // The event is intentionally left to the listener thread/process exit: disposing it
            // here could race with the WaitOne call.
        }
    }
}
