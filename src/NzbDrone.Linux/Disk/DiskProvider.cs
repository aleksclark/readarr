using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Runtime.InteropServices;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnsureThat;

namespace NzbDrone.Linux.Disk
{
    public class DiskProvider : DiskProviderBase
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public DiskProvider()
            : this(new FileSystem())
        {
        }

        public DiskProvider(IFileSystem fileSystem)
            : base(fileSystem)
        {
        }

        public override long? GetAvailableSpace(string path)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);

            var mount = GetMount(path);

            if (mount == null)
            {
                Logger.Debug("Unable to get free space for '{0}', unable to find suitable drive", path);
                return null;
            }

            return mount.AvailableFreeSpace;
        }

        public override long? GetTotalSize(string path)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);

            var mount = GetMount(path);

            return mount?.TotalSize;
        }

        public override void InheritFolderPermissions(string filename)
        {
            // No-op on Linux — permissions are handled by umask
        }

        public override void SetEveryonePermissions(string filename)
        {
            // No-op on Linux
        }

        public override void SetFilePermissions(string path, string mask, string group)
        {
            SetPermissions(path, mask, group);
        }

        public override void SetPermissions(string path, string mask, string group)
        {
            try
            {
                if (uint.TryParse(mask, System.Globalization.NumberStyles.None, null, out _))
                {
                    // Octal permission string like "755"
                    var mode = Convert.ToInt32(mask, 8);
                    NativeMethods.chmod(path, (uint)mode);
                }

                if (!string.IsNullOrWhiteSpace(group))
                {
                    // Try to set group ownership
                    var grp = NativeMethods.getgrnam(group);
                    if (grp != IntPtr.Zero)
                    {
                        var gid = Marshal.ReadInt32(grp, IntPtr.Size + IntPtr.Size); // gr_gid offset
                        NativeMethods.chown(path, unchecked((uint)-1), (uint)gid);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Failed to set permissions on {0}", path);
            }
        }

        public override void CopyPermissions(string sourcePath, string targetPath)
        {
            try
            {
                var sourceInfo = new FileInfo(sourcePath);
                var targetInfo = new FileInfo(targetPath);

                if (sourceInfo.Exists && targetInfo.Exists)
                {
                    // Use .NET 7+ UnixFileMode if available
                    var sourceMode = sourceInfo.UnixFileMode;
                    targetInfo.UnixFileMode = sourceMode;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Failed to copy permissions from {0} to {1}", sourcePath, targetPath);
            }
        }

        public override bool IsValidFolderPermissionMask(string mask)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(mask))
                {
                    return false;
                }

                var mode = Convert.ToInt32(mask, 8);

                // Must have at least full owner permissions (700) = S_IRWXU
                if ((mode & 0x1C0) != 0x1C0)
                {
                    return false;
                }

                // Only allow permission bits (no special bits for 3-digit mask)
                if (mask.Length < 4 && mode > 0x1FF)
                {
                    return false;
                }

                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public override bool TryCreateHardLink(string source, string destination)
        {
            try
            {
                var sourceInfo = new FileInfo(source);

                if (sourceInfo.LinkTarget != null)
                {
                    return false;
                }

                NativeMethods.link(source, destination);
                return File.Exists(destination);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Hardlink '{0}' to '{1}' failed.", source, destination);
                return false;
            }
        }

        public override bool TryRenameFile(string source, string destination)
        {
            return NativeMethods.rename(source, destination) == 0;
        }

        protected override List<IMount> GetAllMounts()
        {
            var mounts = new List<IMount>();

            // Parse /proc/mounts for Linux mount points
            try
            {
                if (File.Exists("/proc/mounts"))
                {
                    var lines = File.ReadAllLines("/proc/mounts");
                    foreach (var line in lines)
                    {
                        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3)
                        {
                            var mountPoint = parts[1].Replace("\\040", " ");
                            var fsType = parts[2];

                            if (IsVirtualFileSystem(fsType))
                            {
                                continue;
                            }

                            try
                            {
                                var driveInfo = _fileSystem.DriveInfo.New(mountPoint);
                                if (driveInfo.IsReady)
                                {
                                    mounts.Add(new DriveInfoMount(driveInfo, FindDriveType.Find(fsType)));
                                }
                            }
                            catch
                            {
                                // Skip inaccessible mounts
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Unable to parse /proc/mounts");
            }

            // Fallback to DriveInfo
            if (!mounts.Any())
            {
                try
                {
                    mounts.AddRange(GetDriveInfoMounts()
                        .Select(d =>
                        {
                            try
                            {
                                return new DriveInfoMount(d, FindDriveType.Find(d.DriveFormat));
                            }
                            catch
                            {
                                return null;
                            }
                        })
                        .Where(d => d is { DriveType: DriveType.Fixed or DriveType.Network or DriveType.Removable }));
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Unable to get drive mounts");
                }
            }

            return mounts.DistinctBy(v => v.RootDirectory).ToList();
        }

        protected override bool IsSpecialMount(IMount mount)
        {
            var root = mount.RootDirectory;

            if (root.StartsWith("/var/lib/"))
            {
                return true;
            }

            if (root.StartsWith("/snap/"))
            {
                return true;
            }

            return false;
        }

        protected override void CopyFileInternal(string source, string destination, bool overwrite = false)
        {
            var sourceInfo = new FileInfo(source);

            if (sourceInfo.LinkTarget != null)
            {
                // Source is a symbolic link — preserve it
                var linkTarget = sourceInfo.LinkTarget;

                if (File.Exists(destination) && overwrite)
                {
                    File.Delete(destination);
                }

                File.CreateSymbolicLink(destination, linkTarget);
            }
            else
            {
                base.CopyFileInternal(source, destination, overwrite);
            }
        }

        protected override void MoveFileInternal(string source, string destination)
        {
            var sourceInfo = new FileInfo(source);

            if (sourceInfo.LinkTarget != null)
            {
                // Source is a symbolic link — recreate at destination
                var linkTarget = sourceInfo.LinkTarget;

                File.CreateSymbolicLink(destination, linkTarget);
                File.Delete(source);
            }
            else
            {
                // Try rename first (atomic on same filesystem)
                if (NativeMethods.rename(source, destination) == 0)
                {
                    return;
                }

                base.MoveFileInternal(source, destination);
            }
        }

        private static bool IsVirtualFileSystem(string fsType)
        {
            return fsType switch
            {
                "sysfs" or "proc" or "devtmpfs" or "devpts" or "tmpfs" or
                "securityfs" or "cgroup" or "cgroup2" or "pstore" or
                "debugfs" or "hugetlbfs" or "mqueue" or "configfs" or
                "fusectl" or "tracefs" or "bpf" or "nsfs" or
                "autofs" or "efivarfs" or "binfmt_misc" or "overlay" => true,
                _ => false
            };
        }

        private static class NativeMethods
        {
            [DllImport("libc", SetLastError = true)]
            public static extern int chmod(string path, uint mode);

            [DllImport("libc", SetLastError = true)]
            public static extern int chown(string path, uint owner, uint group);

            [DllImport("libc", SetLastError = true)]
            public static extern int link(string oldpath, string newpath);

            [DllImport("libc", SetLastError = true)]
            public static extern int rename(string oldpath, string newpath);

            [DllImport("libc", SetLastError = true)]
            public static extern IntPtr getgrnam(string name);
        }
    }
}
