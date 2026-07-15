using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// Creates, lists and restores backups of the SQLite database file.
    /// </summary>
    internal static class BackupHelper
    {
        public const string DbFilePath = @"C:\ProgramData\Kaltenmark-Engineering\TimeTracker\timetracker.db";

        public static readonly string BackupFolderPath = Path.Combine(Path.GetDirectoryName(DbFilePath), "Backups");

        public sealed class BackupFile
        {
            public string FullPath { get; set; }
            public string FileName { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        public static string CreateBackup()
        {
            Directory.CreateDirectory(BackupFolderPath);

            var fileName = $"TimeTrack-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.timetrack-backup";
            var destPath = Path.Combine(BackupFolderPath, fileName);

            File.Copy(DbFilePath, destPath, overwrite: false);

            return destPath;
        }

        public static List<BackupFile> GetAvailableBackups()
        {
            if (!Directory.Exists(BackupFolderPath))
                return new List<BackupFile>();

            return Directory.GetFiles(BackupFolderPath, "*.timetrack-backup")
                .Select(f => new BackupFile
                {
                    FullPath = f,
                    FileName = Path.GetFileName(f),
                    CreatedAt = File.GetLastWriteTime(f)
                })
                .OrderByDescending(b => b.CreatedAt)
                .ToList();
        }

        public static void RestoreBackup(string backupFilePath)
        {
            var walPath = DbFilePath + "-wal";
            var shmPath = DbFilePath + "-shm";

            if (File.Exists(walPath)) File.Delete(walPath);
            if (File.Exists(shmPath)) File.Delete(shmPath);

            var directory = Path.GetDirectoryName(DbFilePath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.Copy(backupFilePath, DbFilePath, overwrite: true);
        }
    }
}
