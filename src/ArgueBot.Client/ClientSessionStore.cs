namespace ArgueBot.Client
{
    using ArgueBot.Core.Configuration;
    using System;
    using System.IO;
    using System.Text.Json;

    /// <summary>Reads and writes the remembered session for an application home in an owner-only file (client-session.json).</summary>
    public static class ClientSessionStore
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };

        /// <summary>Path of the session file for a home.</summary>
        public static string PathFor(string home) { return System.IO.Path.Combine(new ArgueBotPaths(home).Home, "client-session.json"); }

        /// <summary>The remembered session, or null when there is none or it cannot be read.</summary>
        public static ClientSession? Load(string home)
        {
            string path = PathFor(home);
            if (!File.Exists(path)) return null;
            try { return JsonSerializer.Deserialize<ClientSession>(File.ReadAllText(path), Json); }
            catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException) { return null; }
        }

        /// <summary>Remember a session with owner-only permissions, replacing any earlier one.</summary>
        public static void Save(string home, ClientSession session)
        {
            ArgumentNullException.ThrowIfNull(session);
            string path = PathFor(home);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            string temporary = path + ".tmp";
            using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                HomePermissions.File(temporary);
                JsonSerializer.Serialize(stream, session, Json);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
            HomePermissions.File(path);
        }

        /// <summary>Forget the remembered session.</summary>
        public static void Clear(string home)
        {
            string path = PathFor(home);
            if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>True when two service URLs name the same server.</summary>
        public static bool SameServer(string first, string second)
        {
            return Uri.TryCreate(first.TrimEnd('/') + "/", UriKind.Absolute, out Uri? a) && Uri.TryCreate(second.TrimEnd('/') + "/", UriKind.Absolute, out Uri? b) && Uri.Compare(a, b, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
        }
    }
}
