using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MediaFiles
{
    public interface ICollectionFileTransferService
    {
        TransferMode TransferCollectionFile(string sourcePath, string destinationPath, CollectionImportMode importMode);
    }

    public class CollectionFileTransferService : ICollectionFileTransferService
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IDiskTransferService _diskTransferService;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public CollectionFileTransferService(IDiskProvider diskProvider,
                                             IDiskTransferService diskTransferService,
                                             IConfigService configService,
                                             Logger logger)
        {
            _diskProvider = diskProvider;
            _diskTransferService = diskTransferService;
            _configService = configService;
            _logger = logger;
        }

        public TransferMode TransferCollectionFile(string sourcePath, string destinationPath, CollectionImportMode importMode)
        {
            switch (importMode)
            {
                case CollectionImportMode.Hardlink:
                    return TransferWithHardlink(sourcePath, destinationPath);

                case CollectionImportMode.Copy:
                    return TransferWithCopy(sourcePath, destinationPath);

                case CollectionImportMode.Move:
                    _logger.Warn("Move mode used for collection import - source file will be removed: {0}", sourcePath);
                    return _diskTransferService.TransferFile(sourcePath, destinationPath, TransferMode.Move);

                default:
                    return TransferWithHardlink(sourcePath, destinationPath);
            }
        }

        private TransferMode TransferWithHardlink(string sourcePath, string destinationPath)
        {
            // Check if source and destination are on the same filesystem for hardlink viability
            var sourceMount = _diskProvider.GetMount(sourcePath);
            var targetMount = _diskProvider.GetMount(destinationPath);

            var isSameFilesystem = sourceMount != null && targetMount != null &&
                                   sourceMount.RootDirectory == targetMount.RootDirectory;

            if (isSameFilesystem)
            {
                _logger.Debug("Source and destination on same filesystem, attempting hardlink: {0} -> {1}", sourcePath, destinationPath);

                if (_diskProvider.TryCreateHardLink(sourcePath, destinationPath))
                {
                    _logger.Debug("Hardlink created successfully for collection file: {0}", sourcePath);
                    return TransferMode.HardLink;
                }

                _logger.Debug("Hardlink failed, attempting reflink for collection file: {0}", sourcePath);
            }
            else
            {
                _logger.Debug("Source and destination on different filesystems, hardlink not viable: {0}", sourcePath);
            }

            // Fall back to reflink (works on btrfs/xfs/zfs even across different subvolumes)
            var sourceDriveFormat = sourceMount?.DriveFormat ?? string.Empty;
            var targetDriveFormat = targetMount?.DriveFormat ?? string.Empty;
            var isBtrfsOrZfs = (sourceDriveFormat == "btrfs" && targetDriveFormat == "btrfs") ||
                               (sourceDriveFormat == "zfs" && targetDriveFormat == "zfs") ||
                               (sourceDriveFormat == "xfs" && targetDriveFormat == "xfs");

            if (isBtrfsOrZfs)
            {
                _logger.Debug("Attempting reflink for collection file on {0}: {1}", sourceDriveFormat, sourcePath);

                if (_diskProvider.TryCreateRefLink(sourcePath, destinationPath))
                {
                    _logger.Debug("Reflink created successfully for collection file: {0}", sourcePath);
                    return TransferMode.Copy;
                }

                _logger.Debug("Reflink failed, falling back to copy for collection file: {0}", sourcePath);
            }

            // Final fallback: plain copy (never move for collections by default)
            _logger.Debug("Copying collection file (no hardlink/reflink available): {0} -> {1}", sourcePath, destinationPath);
            return _diskTransferService.TransferFile(sourcePath, destinationPath, TransferMode.Copy);
        }

        private TransferMode TransferWithCopy(string sourcePath, string destinationPath)
        {
            // Even in copy mode, try reflink first for efficiency on supported filesystems
            var sourceMount = _diskProvider.GetMount(sourcePath);
            var targetMount = _diskProvider.GetMount(destinationPath);

            var sourceDriveFormat = sourceMount?.DriveFormat ?? string.Empty;
            var targetDriveFormat = targetMount?.DriveFormat ?? string.Empty;
            var isBtrfsOrZfs = (sourceDriveFormat == "btrfs" && targetDriveFormat == "btrfs") ||
                               (sourceDriveFormat == "zfs" && targetDriveFormat == "zfs") ||
                               (sourceDriveFormat == "xfs" && targetDriveFormat == "xfs");

            if (isBtrfsOrZfs)
            {
                if (_diskProvider.TryCreateRefLink(sourcePath, destinationPath))
                {
                    _logger.Debug("Reflink copy created for collection file: {0}", sourcePath);
                    return TransferMode.Copy;
                }
            }

            _logger.Debug("Copying collection file: {0} -> {1}", sourcePath, destinationPath);
            return _diskTransferService.TransferFile(sourcePath, destinationPath, TransferMode.Copy);
        }
    }
}
