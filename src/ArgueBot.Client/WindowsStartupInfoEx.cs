namespace ArgueBot.Client
{
    using System;
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowsStartupInfoEx
    {
        internal WindowsStartupInfo Startup;
        internal IntPtr Attributes;
    }
}
