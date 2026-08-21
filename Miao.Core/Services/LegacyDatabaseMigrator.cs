using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Miao.Core.Data;

namespace Miao.Core.Services
{
    public static class LegacyDatabaseMigrator
    {
        private sealed class TableInfo
        {
            public Dictionary<long, Guid> Ids { get; } = new();
        }

        public static void MigrateIfNeeded(string dbPath)
        {
            if (!File.Exists(dbPath))
                return;

            if (!IsLegacyIntegerIdDatabase(dbPath))
                return;

            var directory = Path.GetDirectoryName(dbPath);
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("Database directory could not be determined.");

            var tempPath = Path.Combine(directory, $"miao-migration-{Guid.NewGuid():N}.db");
            var backupPath = Path.Combine(directory, $"miao-legacy-backup-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");

            try
            {
                using (var newDb = new MiaoDbContext(tempPath))
                {
                    newDb.Database.Migrate();
                    newDb.Database.CloseConnection();
                }

                SqliteConnection.ClearAllPools();

                CopyLegacyData(dbPath, tempPath);
                SqliteConnection.ClearAllPools();

                File.Move(dbPath, backupPath);
                File.Move(tempPath, dbPath);
            }
            catch
            {
                SqliteConnection.ClearAllPools();

                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                throw;
            }
            finally
            {
                SqliteConnection.ClearAllPools();
            }
        }

        private static bool IsLegacyIntegerIdDatabase(string dbPath)
        {
            using var connection = Open(dbPath);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info('Novels');";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(1);
                var type = reader.GetString(2);
                if (string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase))
                    return type.Equals("INTEGER", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static void CopyLegacyData(string sourcePath, string targetPath)
        {
            using var source = Open(sourcePath);
            using var target = Open(targetPath);
            using var transaction = target.BeginTransaction();

            CopyLegacyData(source, target, transaction);
            transaction.Commit();
        }

        private static void CopyLegacyData(SqliteConnection source, SqliteConnection target, SqliteTransaction transaction)
        {
            Execute(target, transaction, "PRAGMA foreign_keys = OFF;");

            var novels = LoadIds(source, "Novels");
            var chapters = LoadIds(source, "Chapters");
            var customLibraries = LoadIds(source, "CustomLibraries");
            var customLibraryNovels = LoadIds(source, "CustomLibraryNovels");
            var novelSources = LoadIds(source, "NovelSources");
            var notes = LoadIds(source, "Notes");
            var tags = LoadIds(source, "Tags");
            var novelTags = LoadIds(source, "NovelTags");
            var novelLinks = LoadIds(source, "NovelLinks");
            var glossarySets = LoadIds(source, "GlossarySets");
            var glossaryEntries = LoadIds(source, "GlossarySetEntries");
            var novelGlossarySets = LoadIds(source, "NovelGlossarySets");
            var volumes = LoadIds(source, "Volumes");

            CopyNovels(source, target, transaction, novels);
            CopySimple(source, target, transaction, "CustomLibraries", new[] { "Id", "Name", "SortOrder" }, customLibraries, null);
            CopySimple(source, target, transaction, "Tags", new[] { "Id", "Name", "Category" }, tags, null);
            CopyVolumes(source, target, transaction, volumes, novels);
            CopyChapters(source, target, transaction, chapters, novels, volumes);
            CopySimple(source, target, transaction, "CustomLibraryNovels", new[] { "Id", "CustomLibraryId", "NovelId" }, customLibraryNovels,
                new Dictionary<string, Func<object?, object?>>
                {
                    ["CustomLibraryId"] = value => MapId(customLibraries, value),
                    ["NovelId"] = value => MapId(novels, value)
                });
            CopySimple(source, target, transaction, "NovelSources", new[] { "Id", "NovelId", "SourceName", "Url", "IsPrimary" }, novelSources,
                new Dictionary<string, Func<object?, object?>> { ["NovelId"] = value => MapId(novels, value) });
            CopySimple(source, target, transaction, "NovelLinks", new[] { "Id", "NovelId", "Description", "Url" }, novelLinks,
                new Dictionary<string, Func<object?, object?>> { ["NovelId"] = value => MapId(novels, value) });
            CopySimple(source, target, transaction, "NovelTags", new[] { "Id", "NovelId", "TagId" }, novelTags,
                new Dictionary<string, Func<object?, object?>>
                {
                    ["NovelId"] = value => MapId(novels, value),
                    ["TagId"] = value => MapId(tags, value)
                });
            CopySimple(source, target, transaction, "Notes", new[] { "Id", "ChapterId", "Content" }, notes,
                new Dictionary<string, Func<object?, object?>> { ["ChapterId"] = value => MapId(chapters, value) });
            CopySimple(source, target, transaction, "GlossarySets", new[] { "Id", "Name", "IsShared", "SortOrder", "OwnerNovelId" }, glossarySets,
                new Dictionary<string, Func<object?, object?>> { ["OwnerNovelId"] = value => MapNullableId(novels, value) });
            CopySimple(source, target, transaction, "GlossarySetEntries", new[] { "Id", "GlossarySetId", "OriginalTerm", "HanViet", "PinYin", "TranslatedTerm" }, glossaryEntries,
                new Dictionary<string, Func<object?, object?>> { ["GlossarySetId"] = value => MapId(glossarySets, value) });
            CopySimple(source, target, transaction, "NovelGlossarySets", new[] { "Id", "NovelId", "GlossarySetId" }, novelGlossarySets,
                new Dictionary<string, Func<object?, object?>>
                {
                    ["NovelId"] = value => MapId(novels, value),
                    ["GlossarySetId"] = value => MapId(glossarySets, value)
                });

            Execute(target, transaction, "PRAGMA foreign_keys = ON;");
        }

        private static void CopyNovels(SqliteConnection source, SqliteConnection target, SqliteTransaction transaction, TableInfo ids)
        {
            using var command = source.CreateCommand();
            command.CommandText = "SELECT Id, Title, TranslatedTitle, Author, SourceUrl, CoverImagePath, Tags, IsFavorite, IsDownloaded, LastReadChapterNumber, AddedAt, LastUpdatedAt, Description, Status, CustomTitle, SourceDescription, TranslatedAuthor FROM Novels ORDER BY Id;";
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var oldId = reader.GetInt64(0);
                var id = ids.Ids[oldId];
                Insert(target, transaction, "Novels",
                    new[] { "Id", "Type", "Title", "TranslatedTitle", "CustomTitle", "Author", "TranslatedAuthor", "SourceUrl", "SourceDescription", "CoverImagePath", "Tags", "Description", "Status", "IsFavorite", "IsDownloaded", "LastReadChapterNumber", "AddedAt", "LastUpdatedAt" },
                    new object?[] { id, 0, reader.GetString(1), reader.GetString(2), reader.GetString(14), reader.GetString(3), reader.GetString(16), reader.GetString(4), reader.GetString(15), reader.GetString(5), reader.GetString(6), reader.GetString(12), reader.GetString(13), reader.GetBoolean(7), reader.GetBoolean(8), reader.GetInt32(9), reader.GetValue(10), reader.IsDBNull(11) ? null : reader.GetValue(11) });
            }
        }

        private static void CopyVolumes(SqliteConnection source, SqliteConnection target, SqliteTransaction transaction, TableInfo ids, TableInfo novels)
        {
            if (!TableExists(source, "Volumes")) return;
            using var command = source.CreateCommand();
            command.CommandText = "SELECT Id, NovelId, Name, SortOrder FROM Volumes ORDER BY Id;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var oldId = reader.GetInt64(0);
                var novelId = reader.GetInt64(1);

                // A few old databases can contain orphaned volumes whose NovelId
                // points to a novel that has already been deleted. Do not create
                // an invalid Volume row in the new database. Removing its mapping
                // also makes any chapter that referenced it fall back to VolumeId=NULL.
                if (!novels.Ids.ContainsKey(novelId))
                {
                    ids.Ids.Remove(oldId);
                    continue;
                }

                Insert(target, transaction, "Volumes", new[] { "Id", "NovelId", "Name", "SortOrder" },
                    new object?[] { ids.Ids[oldId], novels.Ids[novelId], reader.GetString(2), reader.GetInt32(3) });
            }
        }

        private static void CopyChapters(SqliteConnection source, SqliteConnection target, SqliteTransaction transaction, TableInfo ids, TableInfo novels, TableInfo volumes)
        {
            using var command = source.CreateCommand();
            command.CommandText = "SELECT Id, NovelId, Number, Title, TranslatedTitle, OriginalContent, DisplayContent, Status, SourceUrl, DownloadedAt, LastEditedAt, IsPinned, VolumeId FROM Chapters ORDER BY Id;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var volumeId = reader.IsDBNull(12) ? null : MapNullableId(volumes, reader.GetValue(12));
                Insert(target, transaction, "Chapters", new[] { "Id", "NovelId", "VolumeId", "Number", "Title", "TranslatedTitle", "OriginalContent", "DisplayContent", "Status", "SourceUrl", "DownloadedAt", "LastEditedAt", "IsPinned" },
                    new object?[] { ids.Ids[reader.GetInt64(0)], MapId(novels, reader.GetValue(1)), volumeId, reader.GetInt32(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetInt32(7), reader.GetString(8), reader.GetValue(9), reader.IsDBNull(10) ? null : reader.GetValue(10), reader.GetBoolean(11) });
            }
        }

        private static void CopySimple(SqliteConnection source, SqliteConnection target, SqliteTransaction transaction, string table, string[] columns, TableInfo ids, Dictionary<string, Func<object?, object?>>? transforms)
        {
            if (!TableExists(source, table)) return;
            var selectColumns = string.Join(", ", columns.Select(EscapeIdentifier));
            using var command = source.CreateCommand();
            command.CommandText = $"SELECT {selectColumns} FROM {EscapeIdentifier(table)} ORDER BY Id;";
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var values = new object?[columns.Length];
                for (var i = 0; i < columns.Length; i++)
                {
                    var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    if (columns[i] == "Id")
                        value = ids.Ids[Convert.ToInt64(value, CultureInfo.InvariantCulture)];
                    else if (transforms != null && transforms.TryGetValue(columns[i], out var transform))
                        value = transform(value);
                    values[i] = value;
                }
                Insert(target, transaction, table, columns, values);
            }
        }

        private static TableInfo LoadIds(SqliteConnection connection, string table)
        {
            var result = new TableInfo();
            if (!TableExists(connection, table)) return result;

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT Id FROM {EscapeIdentifier(table)};";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                result.Ids[reader.GetInt64(0)] = Guid.NewGuid();
            return result;
        }

        private static object? MapId(TableInfo table, object? value)
        {
            if (value == null || value == DBNull.Value) throw new InvalidOperationException("A required legacy foreign key is NULL.");
            var oldId = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            if (!table.Ids.TryGetValue(oldId, out var id))
                throw new InvalidOperationException($"Legacy foreign key {oldId} could not be mapped.");
            return id;
        }

        private static object? MapNullableId(TableInfo table, object? value)
        {
            if (value == null || value == DBNull.Value) return null;
            return MapId(table, value);
        }

        private static void Insert(SqliteConnection connection, SqliteTransaction transaction, string table, string[] columns, object?[] values)
        {
            var names = string.Join(", ", columns.Select(EscapeIdentifier));
            var parameters = string.Join(", ", columns.Select((_, i) => $"@p{i}"));
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO {EscapeIdentifier(table)} ({names}) VALUES ({parameters});";
            for (var i = 0; i < values.Length; i++)
                command.Parameters.AddWithValue($"@p{i}", values[i] ?? DBNull.Value);
            command.ExecuteNonQuery();
        }

        private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static bool TableExists(SqliteConnection connection, string table)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@name LIMIT 1;";
            command.Parameters.AddWithValue("@name", table);
            return command.ExecuteScalar() != null;
        }

        private static SqliteConnection Open(string path)
        {
            var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            connection.Open();
            return connection;
        }

        private static string EscapeIdentifier(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
