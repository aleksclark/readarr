using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class CollectionFileTransferServiceFixture : CoreTest<CollectionFileTransferService>
    {
        private string _sourcePath;
        private string _destinationPath;

        [SetUp]
        public void Setup()
        {
            _sourcePath = @"C:\Collections\MyBooks\book.epub".AsOsAgnostic();
            _destinationPath = @"C:\Library\Author\book.epub".AsOsAgnostic();
        }

        private Mock<IMount> GivenMount(string rootDirectory, string driveFormat = "ext4")
        {
            var mount = new Mock<IMount>();
            mount.SetupGet(m => m.RootDirectory).Returns(rootDirectory);
            mount.SetupGet(m => m.DriveFormat).Returns(driveFormat);
            return mount;
        }

        private void GivenSameFilesystem(string driveFormat = "ext4")
        {
            var mount = GivenMount("/", driveFormat);

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetMount(_sourcePath))
                .Returns(mount.Object);

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetMount(_destinationPath))
                .Returns(mount.Object);
        }

        private void GivenDifferentFilesystems(string sourceFormat = "ext4", string targetFormat = "ext4")
        {
            var sourceMount = GivenMount("/mnt/source", sourceFormat);
            var targetMount = GivenMount("/mnt/target", targetFormat);

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetMount(_sourcePath))
                .Returns(sourceMount.Object);

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetMount(_destinationPath))
                .Returns(targetMount.Object);
        }

        private void GivenHardlinkSucceeds()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.TryCreateHardLink(_sourcePath, _destinationPath))
                .Returns(true);
        }

        private void GivenHardlinkFails()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.TryCreateHardLink(_sourcePath, _destinationPath))
                .Returns(false);
        }

        private void GivenReflinkSucceeds()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.TryCreateRefLink(_sourcePath, _destinationPath))
                .Returns(true);
        }

        private void GivenReflinkFails()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.TryCreateRefLink(_sourcePath, _destinationPath))
                .Returns(false);
        }

        private void GivenCopyReturns(TransferMode mode = TransferMode.Copy)
        {
            Mocker.GetMock<IDiskTransferService>()
                .Setup(d => d.TransferFile(_sourcePath, _destinationPath, TransferMode.Copy, false))
                .Returns(mode);
        }

        private void GivenMoveReturns(TransferMode mode = TransferMode.Move)
        {
            Mocker.GetMock<IDiskTransferService>()
                .Setup(d => d.TransferFile(_sourcePath, _destinationPath, TransferMode.Move, false))
                .Returns(mode);
        }

        [Test]
        public void should_attempt_hardlink_when_mode_is_hardlink_and_same_filesystem()
        {
            GivenSameFilesystem();
            GivenHardlinkSucceeds();

            var result = Subject.TransferCollectionFile(_sourcePath, _destinationPath, CollectionImportMode.Hardlink);

            result.Should().Be(TransferMode.HardLink);

            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.TryCreateHardLink(_sourcePath, _destinationPath), Times.Once());
        }

        [Test]
        public void should_fall_back_to_copy_when_hardlink_fails()
        {
            GivenSameFilesystem();
            GivenHardlinkFails();
            GivenReflinkFails();
            GivenCopyReturns();

            var result = Subject.TransferCollectionFile(_sourcePath, _destinationPath, CollectionImportMode.Hardlink);

            result.Should().Be(TransferMode.Copy);

            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.TryCreateHardLink(_sourcePath, _destinationPath), Times.Once());

            Mocker.GetMock<IDiskTransferService>()
                .Verify(d => d.TransferFile(_sourcePath, _destinationPath, TransferMode.Copy, false), Times.Once());
        }

        [Test]
        public void should_use_copy_directly_when_mode_is_copy()
        {
            GivenDifferentFilesystems();
            GivenReflinkFails();
            GivenCopyReturns();

            var result = Subject.TransferCollectionFile(_sourcePath, _destinationPath, CollectionImportMode.Copy);

            result.Should().Be(TransferMode.Copy);

            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.TryCreateHardLink(It.IsAny<string>(), It.IsAny<string>()), Times.Never());

            Mocker.GetMock<IDiskTransferService>()
                .Verify(d => d.TransferFile(_sourcePath, _destinationPath, TransferMode.Copy, false), Times.Once());
        }

        [Test]
        public void should_attempt_reflink_before_copy()
        {
            // When both source and target are btrfs, reflink should be attempted
            GivenDifferentFilesystems("btrfs", "btrfs");
            GivenReflinkSucceeds();

            var result = Subject.TransferCollectionFile(_sourcePath, _destinationPath, CollectionImportMode.Copy);

            result.Should().Be(TransferMode.Copy);

            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.TryCreateRefLink(_sourcePath, _destinationPath), Times.Once());

            // Should NOT fall through to disk transfer service copy
            Mocker.GetMock<IDiskTransferService>()
                .Verify(d => d.TransferFile(It.IsAny<string>(), It.IsAny<string>(), TransferMode.Copy, It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_never_delete_source_in_hardlink_mode()
        {
            GivenSameFilesystem();
            GivenHardlinkSucceeds();

            Subject.TransferCollectionFile(_sourcePath, _destinationPath, CollectionImportMode.Hardlink);

            Mocker.GetMock<IDiskTransferService>()
                .Verify(d => d.TransferFile(It.IsAny<string>(), It.IsAny<string>(), TransferMode.Move, It.IsAny<bool>()), Times.Never());

            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.DeleteFile(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_never_delete_source_in_copy_mode()
        {
            GivenDifferentFilesystems();
            GivenReflinkFails();
            GivenCopyReturns();

            Subject.TransferCollectionFile(_sourcePath, _destinationPath, CollectionImportMode.Copy);

            Mocker.GetMock<IDiskTransferService>()
                .Verify(d => d.TransferFile(It.IsAny<string>(), It.IsAny<string>(), TransferMode.Move, It.IsAny<bool>()), Times.Never());

            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.DeleteFile(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_log_warning_when_move_mode_used_with_collection()
        {
            GivenMoveReturns();

            Subject.TransferCollectionFile(_sourcePath, _destinationPath, CollectionImportMode.Move);

            Mocker.GetMock<IDiskTransferService>()
                .Verify(d => d.TransferFile(_sourcePath, _destinationPath, TransferMode.Move, false), Times.Once());

            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
