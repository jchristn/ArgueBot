namespace ArgueBot.Server.Services
{
    using System;
    using System.ComponentModel;
    using System.Runtime.InteropServices;

    internal static class NativeProcessSession
    {
        internal static void Detach()
        {
            if (!OperatingSystem.IsWindows() && CreateSession() < 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "The background service could not detach from the client terminal.");
        }
        [DllImport("libc", EntryPoint = "setsid", SetLastError = true)]
        private static extern int CreateSession();
    }
}
