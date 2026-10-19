namespace ArgueBot.Core.Backups
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class BackupReader
    {
        internal static async IAsyncEnumerable<BackupRecord> ReadAsync(string path, [EnumeratorCancellation] CancellationToken token)
        {
            using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (StreamReader reader = new StreamReader(input, new UTF8Encoding(false, true), false, 4096))
            using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                char[] buffer = new char[4096]; StringBuilder line = new StringBuilder(); bool ended = false; int count;
                while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
                {
                    for (int index = 0; index < count; index++)
                    {
                        if (buffer[index] != '\n')
                        {
                            if (line.Length >= 134217728) throw new InvalidDataException("Backup record exceeds the 128 Mi-character safety limit.");
                            line.Append(buffer[index]); continue;
                        }
                        if (ended) throw new InvalidDataException("Backup contains records after its footer.");
                        string raw = line.ToString(); line.Clear();
                        BackupRecord record = JsonSerializer.Deserialize<BackupRecord>(raw, new JsonSerializerOptions { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow }) ?? throw new InvalidDataException("Backup record cannot be null.");
                        if (record.Kind == "footer")
                        {
                            byte[] expected = Convert.FromHexString(record.Digest);
                            if (!CryptographicOperations.FixedTimeEquals(expected, hash.GetHashAndReset())) throw new InvalidDataException("Backup checksum does not match its content.");
                            ended = true;
                        }
                        else hash.AppendData(Encoding.UTF8.GetBytes(raw + "\n"));
                        yield return record;
                    }
                }
                if (!ended || line.Length > 0) throw new InvalidDataException("Backup is incomplete or lacks its footer.");
            }
        }
    }
}
