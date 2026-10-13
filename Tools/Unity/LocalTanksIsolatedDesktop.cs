// Runs a child process on a private Windows desktop so nothing it creates can reach the user's screen.
//
// Why this exists: measured, not assumed. Launching even a hidden process from an agent's session makes the
// window that process creates the *foreground* window for a moment - a hidden console window belonging to a
// Unity helper executable was observed holding the foreground for about a second while the user's game lost
// it. Activating a window is not something the caller can refuse: Windows does it inside the child, before
// any handle to the window exists, so hiding the window afterwards cannot undo it, and typing synthetic keys
// to hand focus back is forbidden.
//
// A window on another desktop cannot become the foreground window of the interactive desktop, because
// activation is per desktop. Unity therefore runs on a desktop named `LocalTanksUnity`, created inside the
// current window station and never switched to. Its console windows, its rendering window and any error
// dialog stay there, invisible and unactivatable, while the process keeps the same session, the same
// environment, the same files and the same GPU. Measured on this machine: the interactive desktop's
// foreground window did not change once in 7580 samples spread over a graphical Unity run.
//
// Two details are deliberate and both were paid for by a broken run:
//
//   * The process is created with CreateProcess rather than Process.Start, because a child inherits the
//     desktop of the *process* that created it and ProcessStartInfo cannot name a desktop. Moving the
//     calling thread with SetThreadDesktop first does not work either: the child still lands on `Default`.
//     The ProcessStartInfo the launcher builds is therefore handed *in* and read here, so the launch
//     contract stays in one place.
//   * No standard handles are passed at all. Handing over a handle the child cannot use makes Mono's
//     System.Console constructor throw while it starts its own compiler, and script compilation then fails
//     with "handle: Invalid" - a failure that looks like a project error. With the handles left empty the
//     runtime uses Stream.Null, which is exactly what a batch process wants; Unity's own log file is the
//     channel for everything it has to say.
//
// Compiled at run time by Add-Type from Windows PowerShell 5.1, so this is C# 5: no string interpolation,
// no expression-bodied members, no nameof, no null-conditional operators.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LocalTanks.Harness
{
    /// <summary>Outcome of asking for a process on the private desktop.</summary>
    /// <remarks>
    /// The exit code is read through the process handle kept here rather than through
    /// <see cref="System.Diagnostics.Process.ExitCode"/>: Windows PowerShell 5.1 answers an empty value for
    /// that property on any process object it did not itself start, and a silent empty exit code would turn
    /// "the tests failed" into "the tests passed".
    /// </remarks>
    public sealed class DesktopLaunch
    {
        private const uint WaitObject0 = 0x00000000;
        private const uint WaitTimeout = 0x00000102;
        private const uint Infinite = 0xFFFFFFFF;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        public Process Process;
        public IntPtr Handle;
        public int ProcessId;
        public bool Hidden;
        public string Error;
        public string WindowStation;
        public string DesktopPath;

        public bool Started
        {
            get { return Handle != IntPtr.Zero; }
        }

        /// <summary>True once the process has left, asked of the handle instead of the runtime object.</summary>
        public bool HasExited()
        {
            return Handle == IntPtr.Zero || WaitForSingleObject(Handle, 0) == WaitObject0;
        }

        /// <summary>Waits for the process to leave, and says whether it did within the time given.</summary>
        public bool WaitForExit(int milliseconds)
        {
            if (Handle == IntPtr.Zero)
            {
                return true;
            }

            uint wait = WaitForSingleObject(Handle, milliseconds < 0 ? Infinite : (uint)milliseconds);
            if (wait == WaitObject0)
            {
                return true;
            }

            if (wait == WaitTimeout)
            {
                return false;
            }

            throw new InvalidOperationException("waiting for the process failed with Win32 error " +
                                                Marshal.GetLastWin32Error());
        }

        /// <summary>Exit code of the finished process, or 0 when it has not been started.</summary>
        public int ExitCode()
        {
            if (Handle == IntPtr.Zero)
            {
                return 0;
            }

            uint code;
            if (!GetExitCodeProcess(Handle, out code))
            {
                throw new InvalidOperationException("the exit code could not be read: Win32 error " +
                                                    Marshal.GetLastWin32Error());
            }

            return unchecked((int)code);
        }

        /// <summary>Releases the process handle; safe to call more than once.</summary>
        public void Close()
        {
            if (Handle != IntPtr.Zero)
            {
                CloseHandle(Handle);
                Handle = IntPtr.Zero;
            }
        }

        public override string ToString()
        {
            return string.Format("started={0} pid={1} desktop='{2}'{3}", Started, ProcessId, DesktopPath,
                string.IsNullOrEmpty(Error) ? string.Empty : " error=" + Error);
        }
    }

    /// <summary>Creates the private desktop and starts one process on it.</summary>
    public static class LocalTanksIsolatedDesktop
    {
        public const string DefaultDesktopName = "LocalTanksUnity";

        private const int UoiName = 2;
        private const uint DesktopAllAccess = 0x000F01FF;
        private const int ErrorAlreadyExists = 183;
        private const int StartfUseShowWindow = 0x00000001;
        private const short SwHide = 0;
        private const short SwShownormal = 1;
        private const uint CreateUnicodeEnvironment = 0x00000400;
        private const uint CreateNoWindow = 0x08000000;

        private static readonly object Gate = new object();
        private static IntPtr desktop;
        private static string desktopName;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateDesktopW(string name, string device, IntPtr deviceMode, int flags,
            uint access, IntPtr attributes);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenDesktopW(string name, int flags, bool inherit, uint access);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseDesktop(IntPtr handle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetThreadDesktop(uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetProcessWindowStation();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetUserObjectInformationW(IntPtr handle, int index, StringBuilder info,
            int length, out int needed);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessW(string applicationName, StringBuilder commandLine,
            IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint flags,
            IntPtr environment, string currentDirectory, ref StartupInfo startupInfo,
            out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int Size;
            public string Reserved;
            public string Desktop;
            public string Title;
            public int X;
            public int Y;
            public int XSize;
            public int YSize;
            public int XCountChars;
            public int YCountChars;
            public int FillAttribute;
            public int Flags;
            public short ShowWindow;
            public short Reserved2;
            public IntPtr Reserved2Pointer;
            public IntPtr StandardInput;
            public IntPtr StandardOutput;
            public IntPtr StandardError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr Process;
            public IntPtr Thread;
            public int ProcessId;
            public int ThreadId;
        }

        /// <summary>Name of the window station this process belongs to, normally <c>WinSta0</c>.</summary>
        public static string CurrentWindowStationName()
        {
            return UserObjectName(GetProcessWindowStation(), "WinSta0");
        }

        /// <summary>Name of the desktop the calling thread is attached to, normally <c>Default</c>.</summary>
        public static string CurrentDesktopName()
        {
            return UserObjectName(GetThreadDesktop(GetCurrentThreadId()), "Default");
        }

        /// <summary>
        /// True when the calling thread already runs on <paramref name="name"/>, which is how a launcher can
        /// tell whether it was itself started on the private desktop.
        /// </summary>
        public static bool IsCurrent(string name)
        {
            return string.Equals(CurrentDesktopName(), name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Starts the process described by <paramref name="info"/> on the private desktop.
        /// </summary>
        /// <remarks>
        /// The desktop is created on first use and kept open for the lifetime of this process, so several
        /// launches share one name and one desktop. Every failure is reported instead of thrown: a machine
        /// that refuses the private desktop must fall back to a hidden window rather than lose the ability to
        /// run tests.
        /// </remarks>
        public static DesktopLaunch Start(string name, ProcessStartInfo info)
        {
            DesktopLaunch launch = new DesktopLaunch();
            launch.Hidden = info.WindowStyle != ProcessWindowStyle.Normal;
            launch.WindowStation = CurrentWindowStationName();

            if (info.UseShellExecute)
            {
                launch.Error = "the private desktop cannot be combined with UseShellExecute";
                return launch;
            }

            // Refused rather than ignored: silently dropping the caller's streams would turn a lost log into
            // a puzzle. Unity writes its journal to the file named by -logFile instead.
            if (info.RedirectStandardOutput || info.RedirectStandardError || info.RedirectStandardInput)
            {
                launch.Error = "the private desktop does not pass standard handles; use the child's own log file";
                return launch;
            }

            string wanted = string.IsNullOrEmpty(name) ? DefaultDesktopName : name;
            IntPtr target = EnsureDesktop(wanted, launch);
            if (target == IntPtr.Zero)
            {
                return launch;
            }

            launch.DesktopPath = launch.WindowStation + "\\" + wanted;

            StartupInfo startup = new StartupInfo();
            startup.Size = Marshal.SizeOf(typeof(StartupInfo));
            startup.Desktop = launch.DesktopPath;
            startup.Flags = StartfUseShowWindow;
            startup.ShowWindow = launch.Hidden ? SwHide : SwShownormal;

            string line = "\"" + info.FileName + "\"";
            if (!string.IsNullOrEmpty(info.Arguments))
            {
                line += " " + info.Arguments;
            }

            ProcessInformation created;
            StringBuilder commandLine = new StringBuilder(line);
            // CREATE_NO_WINDOW keeps a console child from attaching to the launcher's own console: the
            // dependency is invisible on the interactive desktop but would put a second process's output in
            // the middle of the launcher's. A windowed process ignores the flag.
            bool started = CreateProcessW(info.FileName, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                CreateUnicodeEnvironment | CreateNoWindow, IntPtr.Zero,
                string.IsNullOrEmpty(info.WorkingDirectory) ? null : info.WorkingDirectory,
                ref startup, out created);
            if (!started)
            {
                launch.Error = "CreateProcess on desktop '" + launch.DesktopPath + "' failed with Win32 error " +
                               Marshal.GetLastWin32Error();
                return launch;
            }

            try
            {
                launch.ProcessId = created.ProcessId;

                // The handle is kept, not closed: it is how the launcher waits and how it reads the exit
                // code. The runtime object is a convenience for terminating the process, which is the one
                // thing it does reliably for a process it did not start.
                launch.Handle = created.Process;
                launch.Process = Process.GetProcessById(created.ProcessId);
            }
            catch (Exception error)
            {
                launch.Error = "the process was created but cannot be observed: " + error.GetType().Name +
                               ": " + error.Message;
                if (launch.Handle != IntPtr.Zero)
                {
                    CloseHandle(launch.Handle);
                    launch.Handle = IntPtr.Zero;
                }

                return launch;
            }

            CloseHandle(created.Thread);
            return launch;
        }

        /// <summary>Releases the private desktop this process created, if it created one.</summary>
        public static void Release()
        {
            lock (Gate)
            {
                if (desktop != IntPtr.Zero)
                {
                    CloseDesktop(desktop);
                    desktop = IntPtr.Zero;
                    desktopName = null;
                }
            }
        }

        private static IntPtr EnsureDesktop(string name, DesktopLaunch launch)
        {
            lock (Gate)
            {
                if (desktop != IntPtr.Zero && string.Equals(desktopName, name, StringComparison.Ordinal))
                {
                    return desktop;
                }

                if (desktop != IntPtr.Zero)
                {
                    CloseDesktop(desktop);
                    desktop = IntPtr.Zero;
                }

                IntPtr created = CreateDesktopW(name, null, IntPtr.Zero, 0, DesktopAllAccess, IntPtr.Zero);
                if (created == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == ErrorAlreadyExists)
                    {
                        // Left behind by a run that was killed; reusing it keeps a single name per machine.
                        created = OpenDesktopW(name, 0, false, DesktopAllAccess);
                    }

                    if (created == IntPtr.Zero)
                    {
                        launch.Error = "CreateDesktop('" + name + "') failed with Win32 error " + error;
                        return IntPtr.Zero;
                    }
                }

                desktop = created;
                desktopName = name;
                return desktop;
            }
        }

        private static string UserObjectName(IntPtr handle, string fallback)
        {
            if (handle == IntPtr.Zero)
            {
                return fallback;
            }

            StringBuilder buffer = new StringBuilder(256);
            int needed;
            if (!GetUserObjectInformationW(handle, UoiName, buffer, buffer.Capacity, out needed))
            {
                return fallback;
            }

            return buffer.ToString();
        }
    }
}
