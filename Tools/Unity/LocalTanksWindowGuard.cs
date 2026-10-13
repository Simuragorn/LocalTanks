// Win32 window inspection for the LocalTanks Unity launcher.
//
// Why this exists: Unity is started in batch mode so that agents can compile and test without a visible
// editor, and the launcher must be able to *prove* that no window of the launched process tree ever
// reached the user's foreground. Only Win32 knows that, so the few calls needed are declared here once
// and loaded by both `Invoke-UnityBackground.ps1` (which hides what appears) and
// `Test-UnityForegroundFocus.ps1` (which measures it), instead of being duplicated in each script.
//
// Deliberately absent: SetForegroundWindow, SwitchToThisWindow and every form of synthetic input. The
// launcher never moves focus back to the user; it only reports that it was taken. Hiding a window with
// ShowWindow(SW_HIDE) is safe for the same reason - it changes visibility, not activation.
//
// This file is compiled at run time by Add-Type, which on Windows PowerShell 5.1 uses the .NET Framework
// C# compiler, so it is written in C# 5: no string interpolation, no expression-bodied members, no
// nameof, no null-conditional operators.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LocalTanks.Harness
{
    /// <summary>One top-level window, as observed at a point in time.</summary>
    public sealed class WindowSighting
    {
        public IntPtr Handle;
        public int ProcessId;
        public string ProcessName;
        public string ClassName;
        public string Title;
        public bool Visible;

        public override string ToString()
        {
            return string.Format("pid={0} ({1}) class={2} visible={3} title='{4}'",
                ProcessId, ProcessName, ClassName, Visible, Title);
        }
    }

    /// <summary>Snapshot of the foreground window, so a change can be attributed to a process.</summary>
    public sealed class ForegroundInfo
    {
        public IntPtr Handle;
        public int ProcessId;
        public string ProcessName;
        public string ClassName;
        public string Title;

        public override string ToString()
        {
            return string.Format("pid={0} ({1}) class={2} title='{3}'",
                ProcessId, ProcessName, ClassName, Title);
        }
    }

    /// <summary>
    /// Read-only inspection of top-level windows plus the one write the harness needs: hiding a window
    /// that belongs to the process tree the launcher itself started.
    /// </summary>
    public static class LocalTanksWindowGuard
    {
        public const int SwHide = 0;

        private const uint Th32CsSnapProcess = 0x00000002;
        private const int MaxClassName = 256;
        private const int MaxTitle = 512;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int command);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder buffer, int count);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder buffer, int count);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry
        {
            public uint Size;
            public uint Usage;
            public uint ProcessId;
            public IntPtr DefaultHeapId;
            public uint ModuleId;
            public uint Threads;
            public uint ParentProcessId;
            public int PriorityClassBase;
            public uint Flags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string ExeFile;
        }

        /// <summary>Handle of the window that currently owns the user's input, or <see cref="IntPtr.Zero"/>.</summary>
        public static IntPtr ForegroundHandle()
        {
            return GetForegroundWindow();
        }

        public static int WindowProcessId(IntPtr hWnd)
        {
            uint processId;
            GetWindowThreadProcessId(hWnd, out processId);
            return unchecked((int)processId);
        }

        /// <summary>Everything the launcher needs to name the window that owns the foreground.</summary>
        public static ForegroundInfo DescribeForeground()
        {
            IntPtr handle = GetForegroundWindow();
            ForegroundInfo info = new ForegroundInfo();
            info.Handle = handle;
            info.ProcessId = WindowProcessId(handle);
            info.ProcessName = ProcessName(info.ProcessId);
            info.ClassName = ClassName(handle);
            info.Title = Title(handle);
            return info;
        }

        /// <summary>
        /// Every top-level window owned by <paramref name="rootProcessId"/> or by a process it spawned.
        /// </summary>
        /// <remarks>
        /// Descendants matter: an editor in batch mode runs helper executables (the shader compiler among
        /// them), and a console window is created by the child, not by Unity itself. Checking only the
        /// process the launcher started would miss exactly the window a user notices.
        /// </remarks>
        public static WindowSighting[] TopLevelWindows(int rootProcessId)
        {
            HashSet<int> tree = new HashSet<int>(ProcessTree(rootProcessId));
            List<WindowSighting> found = new List<WindowSighting>();
            EnumWindowsProc callback = delegate(IntPtr hWnd, IntPtr lParam)
            {
                int processId = WindowProcessId(hWnd);
                if (tree.Contains(processId))
                {
                    found.Add(Sighting(hWnd, processId));
                }

                return true;
            };

            EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            return found.ToArray();
        }

        /// <summary>
        /// Hides every visible top-level window of the tree and reports how many were hidden.
        /// </summary>
        /// <remarks>
        /// <c>ShowWindow(hWnd, SW_HIDE)</c> never activates the window and never touches the foreground
        /// window, which is why it is the only window-changing call the launcher makes. The count is
        /// returned rather than logged because a non-zero count is evidence the process created a window at
        /// all, and the launcher records it as such.
        /// </remarks>
        public static int HideTreeWindows(int rootProcessId)
        {
            int hidden = 0;
            foreach (WindowSighting sighting in TopLevelWindows(rootProcessId))
            {
                if (sighting.Visible && ShowWindow(sighting.Handle, SwHide))
                {
                    hidden++;
                }
            }

            return hidden;
        }

        /// <summary>
        /// <paramref name="rootProcessId"/> together with every process that descends from it, following
        /// parent links in a single snapshot of the process table.
        /// </summary>
        public static int[] ProcessTree(int rootProcessId)
        {
            Dictionary<int, int> parents = ParentMap();
            List<int> tree = new List<int>();
            tree.Add(rootProcessId);
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (KeyValuePair<int, int> link in parents)
                {
                    if (tree.Contains(link.Key))
                    {
                        continue;
                    }

                    if (tree.Contains(link.Value))
                    {
                        tree.Add(link.Key);
                        grew = true;
                    }
                }
            }

            return tree.ToArray();
        }

        /// <summary>Parent process of a single process, or 0 when it is unknown or already gone.</summary>
        public static int ParentProcessId(int processId)
        {
            int parent;
            return ParentMap().TryGetValue(processId, out parent) ? parent : 0;
        }

        /// <summary>Executable name of a process, or an empty string when it has already exited.</summary>
        public static string ProcessName(int processId)
        {
            if (processId <= 0)
            {
                return string.Empty;
            }

            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    return process.ProcessName;
                }
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
            catch (InvalidOperationException)
            {
                return string.Empty;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Protected and elevated processes refuse the query; an unnamed foreground window is a
                // worse report than failing the launcher over it.
                return string.Empty;
            }
        }

        /// <summary>Window class, which is how a console window is told from a rendering surface.</summary>
        public static string ClassName(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return string.Empty;
            }

            StringBuilder buffer = new StringBuilder(MaxClassName);
            GetClassName(hWnd, buffer, buffer.Capacity);
            return buffer.ToString();
        }

        public static string Title(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return string.Empty;
            }

            StringBuilder buffer = new StringBuilder(MaxTitle);
            GetWindowText(hWnd, buffer, buffer.Capacity);
            return buffer.ToString();
        }

        /// <summary>Class name a console window created by a console subsystem executable carries.</summary>
        public static bool IsConsoleWindow(WindowSighting sighting)
        {
            return sighting != null && string.Equals(sighting.ClassName, "ConsoleWindowClass", StringComparison.Ordinal);
        }

        private static WindowSighting Sighting(IntPtr hWnd, int processId)
        {
            WindowSighting sighting = new WindowSighting();
            sighting.Handle = hWnd;
            sighting.ProcessId = processId;
            sighting.ProcessName = ProcessName(processId);
            sighting.ClassName = ClassName(hWnd);
            sighting.Title = Title(hWnd);
            sighting.Visible = IsWindowVisible(hWnd);
            return sighting;
        }

        private static Dictionary<int, int> ParentMap()
        {
            Dictionary<int, int> parents = new Dictionary<int, int>();
            IntPtr snapshot = CreateToolhelp32Snapshot(Th32CsSnapProcess, 0);
            if (snapshot == new IntPtr(-1))
            {
                return parents;
            }

            try
            {
                ProcessEntry entry = new ProcessEntry();
                entry.Size = (uint)Marshal.SizeOf(typeof(ProcessEntry));
                if (!Process32First(snapshot, ref entry))
                {
                    return parents;
                }

                do
                {
                    parents[unchecked((int)entry.ProcessId)] = unchecked((int)entry.ParentProcessId);
                }
                while (Process32Next(snapshot, ref entry));
            }
            finally
            {
                CloseHandle(snapshot);
            }

            return parents;
        }
    }
}
