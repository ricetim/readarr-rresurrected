using System;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles
{
    public interface IUpgradeMediaFiles
    {
        BookFileMoveResult UpgradeBookFile(BookFile bookFile, LocalBook localBook, bool copyOnly = false);
    }

    public class UpgradeMediaFileService : IUpgradeMediaFiles
    {
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMetadataTagService _metadataTagService;
        private readonly IMoveBookFiles _bookFileMover;
        private readonly IDiskProvider _diskProvider;
        private readonly IRootFolderService _rootFolderService;
        private readonly ICalibreProxy _calibre;
        private readonly Logger _logger;

        public UpgradeMediaFileService(IRecycleBinProvider recycleBinProvider,
                                       IMediaFileService mediaFileService,
                                       IMetadataTagService metadataTagService,
                                       IMoveBookFiles bookFileMover,
                                       IDiskProvider diskProvider,
                                       IRootFolderService rootFolderService,
                                       ICalibreProxy calibre,
                                       Logger logger)
        {
            _recycleBinProvider = recycleBinProvider;
            _mediaFileService = mediaFileService;
            _metadataTagService = metadataTagService;
            _bookFileMover = bookFileMover;
            _diskProvider = diskProvider;
            _rootFolderService = rootFolderService;
            _calibre = calibre;
            _logger = logger;
        }

        public BookFileMoveResult UpgradeBookFile(BookFile bookFile, LocalBook localBook, bool copyOnly = false)
        {
            var moveFileResult = new BookFileMoveResult();
            var allExistingFiles = localBook.Book.BookFiles.Value;

            // Ebooks and audiobooks live side by side, and each keeps one copy. An incoming
            // file replaces the existing files of its own media type and never touches the
            // other, so importing an ebook no longer wipes out the audiobook.
            var replacedFiles = allExistingFiles
                .Where(f => IsSameMediaType(f.Quality, bookFile.Quality))
                .ToList();

            var rootFolderPath = _diskProvider.GetParentFolder(localBook.Author.Path);
            var rootFolder = _rootFolderService.GetBestRootFolder(rootFolderPath);
            var isCalibre = rootFolder.IsCalibreLibrary && rootFolder.CalibreSettings != null;

            var settings = rootFolder.CalibreSettings;

            // If there are existing book files and the root folder is missing, throw, so the old file isn't left behind during the import process.
            if (allExistingFiles.Any() && !_diskProvider.FolderExists(rootFolderPath))
            {
                throw new RootFolderNotFoundException($"Root folder '{rootFolderPath}' was not found.");
            }

            // A calibre record holds every format of a book, so the new file joins whichever
            // record already exists even when no file of its own format is being replaced.
            var existingCalibreId = allExistingFiles.FirstOrDefault(f => f.CalibreId != 0)?.CalibreId ?? 0;

            if (existingCalibreId != 0)
            {
                bookFile.CalibreId = existingCalibreId;
            }

            foreach (var file in replacedFiles)
            {
                var bookFilePath = file.Path;
                var subfolder = rootFolderPath.GetRelativePath(_diskProvider.GetParentFolder(bookFilePath));

                bookFile.CalibreId = file.CalibreId;

                if (_diskProvider.FileExists(bookFilePath))
                {
                    _logger.Debug("Removing existing book file: {0} CalibreId: {1}", file, file.CalibreId);

                    if (!isCalibre)
                    {
                        _recycleBinProvider.DeleteFile(bookFilePath, subfolder);
                    }
                    else
                    {
                        // Only drop the format being replaced. Removing every format would take
                        // the audiobook down with the ebook, or the other way round.
                        var replacedFormat = Path.GetExtension(bookFilePath).TrimStart('.').ToUpperInvariant();
                        var existing = _calibre.GetBook(file.CalibreId, settings);
                        var existingFormats = existing.Formats.Keys
                            .Where(f => f.Equals(replacedFormat, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (existingFormats.Any())
                        {
                            _logger.Debug($"Removing existing formats {existingFormats.ConcatToString()} from calibre");
                            _calibre.RemoveFormats(file.CalibreId, existingFormats, settings);
                        }
                    }
                }

                moveFileResult.OldFiles.Add(file);
                _mediaFileService.Delete(file, DeleteMediaFileReason.Upgrade);
            }

            if (!isCalibre)
            {
                if (copyOnly)
                {
                    moveFileResult.BookFile = _bookFileMover.CopyBookFile(bookFile, localBook);
                }
                else
                {
                    moveFileResult.BookFile = _bookFileMover.MoveBookFile(bookFile, localBook);
                }

                _metadataTagService.WriteTags(bookFile, true);
            }
            else
            {
                var source = bookFile.Path;

                moveFileResult.BookFile = _calibre.AddAndConvert(bookFile, settings);

                if (!copyOnly)
                {
                    _diskProvider.DeleteFile(source);
                }
            }

            return moveFileResult;
        }

        private static bool IsSameMediaType(QualityModel left, QualityModel right)
        {
            // A null quality can only be matched against another null one; treating unknown as
            // a match for everything would bring back the cross-type deletion this guards.
            if (left?.Quality == null || right?.Quality == null)
            {
                return left?.Quality == null && right?.Quality == null;
            }

            return left.Quality.MediaType == right.Quality.MediaType;
        }
    }
}
