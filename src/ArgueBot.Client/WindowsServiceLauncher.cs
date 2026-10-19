namespace ArgueBot.Client
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using Microsoft.Win32.SafeHandles;

    internal static class WindowsServiceLauncher
    {
        internal static Process Start(ProcessStartInfo start)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            WindowsSecurityAttributes security = new WindowsSecurityAttributes { Size = (uint)Marshal.SizeOf<WindowsSecurityAttributes>(), Inherit = 1 };
            using (SafeFileHandle nullDevice = CreateFile("NUL", 0xC0000000, 3, ref security, 3, 0x80, IntPtr.Zero))
            {
                if (nullDevice.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError(), "The service null-device handle could not be opened.");
                IntPtr bytes = IntPtr.Zero;
                InitializeAttributes(IntPtr.Zero, 1, 0, ref bytes);
                IntPtr attributes = Marshal.AllocHGlobal(bytes);
                IntPtr handles = IntPtr.Zero;
                bool initialized = false;
                try
                {
                    handles = Marshal.AllocHGlobal(IntPtr.Size);
                    if (!InitializeAttributes(attributes, 1, 0, ref bytes)) throw new Win32Exception(Marshal.GetLastPInvokeError(), "The service handle whitelist could not be initialized.");
                    initialized = true;
                    Marshal.WriteIntPtr(handles, nullDevice.DangerousGetHandle());
                    if (!UpdateAttribute(attributes, 0, new IntPtr(0x00020002), handles, new IntPtr(IntPtr.Size), IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastPInvokeError(), "The service handle whitelist could not be configured.");
                    WindowsStartupInfoEx startup = new WindowsStartupInfoEx { Attributes = attributes, Startup = new WindowsStartupInfo { Size = (uint)Marshal.SizeOf<WindowsStartupInfoEx>(), Flags = 0x100, Input = nullDevice.DangerousGetHandle(), Output = nullDevice.DangerousGetHandle(), Error = nullDevice.DangerousGetHandle() } };
                    StringBuilder command = new StringBuilder(Quote(start.FileName) + " " + string.Join(" ", start.ArgumentList.Select(Quote)));
                    // Only the null-device handle is inherited. Client capture pipes and console handles must not keep callers alive.
                    if (!CreateProcess(null, command, IntPtr.Zero, IntPtr.Zero, true, 0x00080008, IntPtr.Zero, string.IsNullOrEmpty(start.WorkingDirectory) ? null : start.WorkingDirectory, ref startup, out WindowsProcessInformation child)) throw new Win32Exception(Marshal.GetLastPInvokeError(), "The background Windows service could not start.");
                    try { return Process.GetProcessById(checked((int)child.ProcessId)); }
                    finally { CloseHandle(child.Thread); CloseHandle(child.Process); GC.KeepAlive(nullDevice); }
                }
                finally
                {
                    if (initialized) DeleteAttributes(attributes);
                    if (handles != IntPtr.Zero) Marshal.FreeHGlobal(handles); Marshal.FreeHGlobal(attributes);
                }
            }
        }
        private static string Quote(string value)
        {
            if (value.Length > 0 && !value.Any(character => char.IsWhiteSpace(character) || character == '"')) return value;
            StringBuilder quoted = new StringBuilder("\""); int slashes = 0;
            foreach (char character in value)
            {
                if (character == '\\') { slashes++; continue; }
                quoted.Append('\\', character == '"' ? slashes * 2 + 1 : slashes); slashes = 0; quoted.Append(character);
            }
            quoted.Append('\\', slashes * 2); quoted.Append('"'); return quoted.ToString();
        }
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, ref WindowsSecurityAttributes attributes, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", EntryPoint = "InitializeProcThreadAttributeList", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool InitializeAttributes(IntPtr list, int count, uint flags, ref IntPtr bytes);
        [DllImport("kernel32.dll", EntryPoint = "UpdateProcThreadAttribute", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UpdateAttribute(IntPtr list, uint flags, IntPtr key, IntPtr value, IntPtr bytes, IntPtr previous, IntPtr returned);
        [DllImport("kernel32.dll", EntryPoint = "DeleteProcThreadAttributeList")]
        private static extern void DeleteAttributes(IntPtr list);
        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateProcess(string? application, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, IntPtr environment, string? directory, ref WindowsStartupInfoEx startup, out WindowsProcessInformation information);
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
