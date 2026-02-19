// BuildConsole.cs - Opens a Windows console window in builds
// Redirects Debug.Log output to a visible terminal
// TEMPORARY: Remove after debugging

using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using UnityEngine;

namespace MOBANet.UnityView.UI
{
    /// <summary>
    /// Opens a native Windows console window alongside the build.
    /// All Debug.Log/Warning/Error output appears in the terminal.
    /// Only activates in builds (not in Editor).
    /// </summary>
    public class BuildConsole : MonoBehaviour
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        private static extern bool SetConsoleTitleW([MarshalAs(UnmanagedType.LPWStr)] string title);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetStdHandle(int nStdHandle, IntPtr handle);

        private const int STD_OUTPUT_HANDLE = -11;

        void Awake()
        {
            AllocConsole();

            // Redirect stdout to the new console
            IntPtr stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new SafeFileHandle(stdHandle, false);
            var fs = new FileStream(safeHandle, FileAccess.Write);
            var writer = new StreamWriter(fs) { AutoFlush = true };
            Console.SetOut(writer);

            SetConsoleTitleW("Ryvax Client Console");
            Application.logMessageReceived += OnLogMessage;
            Console.WriteLine("[BuildConsole] Console opened");
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLogMessage;
        }

        private void OnLogMessage(string message, string stackTrace, LogType type)
        {
            string prefix = type switch
            {
                LogType.Error => "[ERR] ",
                LogType.Warning => "[WRN] ",
                LogType.Exception => "[EXC] ",
                _ => ""
            };
            Console.WriteLine($"{prefix}{message}");
        }
#endif
    }
}
