namespace ArgueBot.Client
{
    using System;
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowsSecurityAttributes
    {
        internal uint Size;
        internal IntPtr Descriptor;
        internal int Inherit;
    }
}
