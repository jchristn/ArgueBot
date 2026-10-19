namespace ArgueBot.Core.Backups
{
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Data;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text.Json;
    using System.Text;
    using System.Threading.Tasks;
    using System.Threading;
    using System;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    internal sealed class LogicalSnapshot
    {
        private readonly DatabaseDriverBase _Database;
        private readonly DatabaseTypeEnum _Provider;
        internal LogicalSnapshot(DatabaseDriverBase database, DatabaseTypeEnum provider) { _Database = database; _Provider = provider; }
        internal async Task ExportAsync(string destination, CancellationToken token)
        {
            if (File.Exists(destination)) throw new IOException("Backup destination already exists.");
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                FileStreamOptions options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (FileStream output = new FileStream(temporary, options))
                using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
                using (DbTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    HomePermissions.File(temporary);
                    int version = Convert.ToInt32(await ScalarAsync(connection, transaction, "SELECT MAX(version) FROM schema_migrations", token).ConfigureAwait(false), CultureInfo.InvariantCulture);
                    await RecordAsync(output, hash, new BackupRecord { Kind = "header", SchemaVersion = version, Provider = _Provider.ToString(), CreatedUtc = DateTime.UtcNow }, token).ConfigureAwait(false);
                    long rows = 0;
                    foreach (string table in BackupSchema.Tables().Keys)
                    {
                        if (BackupSchema.IntroducedVersion(table) > version) continue;
                        using (DbCommand command = Command(connection, transaction, "SELECT * FROM " + table + " ORDER BY " + (table == "schema_migrations" ? "version" : "id")))
                        using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            List<string> columns = new List<string>(); List<int> ordinals = new List<int>();
                            Dictionary<string, bool> declared = BackupSchema.ColumnsAtVersion(table, version);
                            for (int index = 0; index < reader.FieldCount; index++)
                            {
                                string column = reader.GetName(index);
                                if (declared.ContainsKey(BackupSchema.Normalize(table, column))) { columns.Add(column); ordinals.Add(index); }
                            }
                            await RecordAsync(output, hash, new BackupRecord { Kind = "table", Table = table, Columns = columns }, token).ConfigureAwait(false);
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                            {
                                List<string?> values = new List<string?>();
                                foreach (int index in ordinals) values.Add(reader.IsDBNull(index) ? null : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture));
                                await RecordAsync(output, hash, new BackupRecord { Kind = "row", Table = table, Values = values }, token).ConfigureAwait(false); rows++;
                            }
                        }
                    }
                    transaction.Commit();
                    await RecordAsync(output, null, new BackupRecord { Kind = "footer", TotalRows = rows, Digest = Convert.ToHexString(hash.GetHashAndReset()) }, token).ConfigureAwait(false);
                    await output.FlushAsync(token).ConfigureAwait(false); output.Flush(true);
                }
                File.Move(temporary, destination, false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private static async Task RecordAsync(Stream output, IncrementalHash? hash, BackupRecord record, CancellationToken token)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record) + "\n");
            hash?.AppendData(bytes);
            await output.WriteAsync(bytes.AsMemory(), token).ConfigureAwait(false);
        }
        internal static async Task ValidateAsync(string path, CancellationToken token)
        {
            Dictionary<string, Dictionary<string, bool>> schema = BackupSchema.Tables();
            List<string> order = schema.Keys.ToList(); HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            string table = ""; List<string> columns = new List<string>(); int version = 0; long rows = 0; bool header = false; bool footer = false; int position = -1;
            await foreach (BackupRecord record in BackupReader.ReadAsync(path, token).ConfigureAwait(false))
            {
                if (!header)
                {
                    if (record.Kind != "header" || record.FormatVersion != 1 || record.SchemaVersion < 1 || record.SchemaVersion > DatabaseSchema.CurrentVersion || !Enum.TryParse(record.Provider, out DatabaseTypeEnum provider) || !Enum.IsDefined(provider)) throw new InvalidDataException("Unsupported backup format, schema or provider.");
                    header = true; version = record.SchemaVersion; continue;
                }
                if (record.Kind == "table")
                {
                    if (!schema.ContainsKey(record.Table) || !seen.Add(record.Table) || order.IndexOf(record.Table) <= position || record.Columns == null) throw new InvalidDataException("Backup table inventory is invalid.");
                    position = order.IndexOf(record.Table); table = record.Table; columns = record.Columns.Select(column => BackupSchema.Normalize(table, column)).ToList();
                    if (columns.Count != columns.Distinct(StringComparer.Ordinal).Count() || !columns.OrderBy(column => column, StringComparer.Ordinal).SequenceEqual(BackupSchema.ColumnsAtVersion(table, version).Keys.OrderBy(column => column, StringComparer.Ordinal))) throw new InvalidDataException("Backup column inventory does not match its declared schema.");
                }
                else if (record.Kind == "row")
                {
                    if (record.Table != table || table.Length == 0 || record.Values == null || record.Values.Count != columns.Count) throw new InvalidDataException("Backup row shape is invalid.");
                    for (int index = 0; index < columns.Count; index++) if (schema[table][columns[index]] && record.Values[index] != null && !long.TryParse(record.Values[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out long _)) throw new InvalidDataException("Backup integer value is invalid.");
                    rows = checked(rows + 1);
                }
                else if (record.Kind == "footer")
                {
                    if (record.TotalRows != rows) throw new InvalidDataException("Backup row count does not match its footer.");
                    footer = true;
                }
                else throw new InvalidDataException("Unexpected backup record.");
            }
            IEnumerable<string> required = order.Where(name => BackupSchema.IntroducedVersion(name) <= version);
            if (!header || !footer || required.Any(name => !seen.Contains(name))) throw new InvalidDataException("Backup is missing required tables or records.");
        }
        internal static async Task RestoreAsync(DbConnection connection, DbTransaction transaction, string path, CancellationToken token)
        {
            Dictionary<string, Dictionary<string, bool>> schema = BackupSchema.Tables();
            foreach (string table in schema.Keys.Reverse()) await ExecuteAsync(connection, transaction, "DELETE FROM " + table, token).ConfigureAwait(false);
            string current = ""; List<string> columns = new List<string>();
            await foreach (BackupRecord record in BackupReader.ReadAsync(path, token).ConfigureAwait(false))
            {
                if (record.Kind == "table") { current = record.Table; columns = record.Columns.Select(column => BackupSchema.Normalize(current, column)).ToList(); }
                if (record.Kind != "row") continue;
                Dictionary<string, object?> values = new Dictionary<string, object?>(); List<string> placeholders = new List<string>();
                for (int index = 0; index < columns.Count; index++)
                {
                    string parameter = "cell" + index.ToString(CultureInfo.InvariantCulture); placeholders.Add("@" + parameter);
                    values.Add(parameter, record.Values[index] == null ? null : schema[current][columns[index]] ? long.Parse(record.Values[index]!, CultureInfo.InvariantCulture) : record.Values[index]);
                }
                await ExecuteAsync(connection, transaction, "INSERT INTO " + current + "(" + string.Join(",", columns) + ") VALUES(" + string.Join(",", placeholders) + ")", token, values).ConfigureAwait(false);
            }
        }
    }
}
