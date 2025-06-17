using System;
using System.IO;

namespace VCDevTool.Shared
{
    /// <summary>
    /// Common helper methods for normalising paths so that the locking logic
    /// is 100% consistent between API and client.  Always use these helpers
    /// when constructing or comparing file-lock keys.
    /// </summary>
    public static class PathUtils
    {
        /// <summary>
        /// Normalises a filesystem path for use as a database lock key.
        /// <list type="bullet">
        /// <item>Trims whitespace.</item>
        /// <item>Removes a trailing slash or back-slash.</item>
        /// <item>Converts back-slashes to forward-slashes.</item>
        /// <item>Converts to lower-case using <see cref="String.ToLowerInvariant"/>.</item>
        /// </list>
        /// This guarantees that variants such as
        /// <c>Y:\Test\VDB_Test\\</c> and <c>y:/test/vdb_test</c> map to exactly the same key.
        /// </summary>
        /// <param name="path">Original path (local folder, UNC path, etc.).</param>
        /// <returns>Normalised path string.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is null or whitespace.</exception>
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            path = path.Trim();

            // Remove any trailing directory separators so Y:/Data == Y:/Data/
            path = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // Replace back-slashes with forward-slashes and lower-case
            return path.Replace('\\', '/').ToLowerInvariant();
        }

        /// <summary>
        /// Builds the full key that is persisted in the <c>FileLocks</c> table for a folder lock.
        /// </summary>
        /// <param name="folderPath">Already normalized folder path</param>
        public static string GetFolderLockKey(string folderPath)
        {
            // Note: We assume the path has already been normalized by the caller
            return $"folder_lock:{folderPath}";
        }
    }
} 