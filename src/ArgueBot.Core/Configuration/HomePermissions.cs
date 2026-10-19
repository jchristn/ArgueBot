namespace ArgueBot.Core.Configuration
{
    using System;
    using System.IO;
    using System.Security.AccessControl;
    using System.Security.Principal;

    /// <summary>Owner-only filesystem permissions for native application state.</summary>
    public static class HomePermissions
    {
        /// <summary>Protect a directory and make new children inherit the current Windows owner's access.</summary>
        public static void Directory(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    SecurityIdentifier owner = identity.User ?? throw new UnauthorizedAccessException("The current Windows identity has no user SID.");
                    DirectorySecurity security = new DirectorySecurity();
                    security.SetAccessRuleProtection(true, false); security.SetOwner(owner);
                    security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                    new DirectoryInfo(path).SetAccessControl(security);
                }
            }
            else System.IO.File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        /// <summary>Protect a file that may be outside an already protected application directory.</summary>
        public static void File(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    SecurityIdentifier owner = identity.User ?? throw new UnauthorizedAccessException("The current Windows identity has no user SID.");
                    FileSecurity security = new FileSecurity();
                    security.SetAccessRuleProtection(true, false); security.SetOwner(owner);
                    security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
                    new FileInfo(path).SetAccessControl(security);
                }
            }
            else System.IO.File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
