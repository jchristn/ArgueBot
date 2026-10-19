namespace ArgueBot.Client
{
    using System;
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowsStartupInfo
    {
        internal uint Size;
        internal IntPtr Reserved;
        internal IntPtr Desktop;
        internal IntPtr Title;
        internal uint X;
        internal uint Y;
        internal uint Width;
        internal uint Height;
        internal uint Columns;
        internal uint Rows;
        internal uint Fill;
        internal uint Flags;
        internal ushort ShowWindow;
        internal ushort ReservedBytes;
        internal IntPtr ReservedData;
        internal IntPtr Input;
        internal IntPtr Output;
        internal IntPtr Error;
    }
}
