namespace ArgueBot.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;

    /// <summary>Resource naming, size and tag rules shared by storage, the API and agent tools.</summary>
    public static class ResourceRules
    {
        /// <summary>Longest resource name, in characters.</summary>
        public const int MaxNameLength = 128;
        /// <summary>Longest title, in characters.</summary>
        public const int MaxTitleLength = 200;
        /// <summary>Longest description, in characters.</summary>
        public const int MaxDescriptionLength = 500;
        /// <summary>Most tags on one resource.</summary>
        public const int MaxTags = 16;
        /// <summary>Longest tag, in characters.</summary>
        public const int MaxTagLength = 64;
        /// <summary>Largest body, in UTF-8 bytes (256 KiB).</summary>
        public const int MaxBodyBytes = 262144;
        /// <summary>Revisions kept per resource. Older revisions are removed when a change is saved.</summary>
        public const int MaxRevisionsKept = 100;
        /// <summary>Most resources in one tenant.</summary>
        public const int MaxResourcesPerTenant = 10000;
        /// <summary>Most files in one import.</summary>
        public const int MaxImportFiles = 50;

        /// <summary>Normalize a resource name to lowercase and validate it.</summary>
        /// <param name="name">Name supplied by a user, file or agent.</param>
        /// <param name="field">Request field reported on failure.</param>
        /// <returns>The lowercase name.</returns>
        /// <exception cref="DomainException">400 invalid_resource_name when the name breaks the naming rules.</exception>
        public static string NormalizeName(string? name, string field = "name")
        {
            if (TryNormalizeName(name, out string normalized)) return normalized;
            throw new DomainException(400, "invalid_resource_name", "Use 1 to 128 characters: letters, digits, '-', '_', '.' and '/'. Start with a letter or digit, and do not use empty, '.' or '..' path segments.", field);
        }

        /// <summary>Normalize a resource name to lowercase, reporting whether it is valid.</summary>
        /// <param name="name">Candidate name.</param>
        /// <param name="normalized">The lowercase name, or empty when invalid.</param>
        /// <returns>True when the name is valid.</returns>
        public static bool TryNormalizeName(string? name, out string normalized)
        {
            normalized = "";
            if (name == null) return false;
            string value = name.Trim().ToLowerInvariant();
            if (value.Length < 1 || value.Length > MaxNameLength) return false;
            if (!char.IsAsciiLetterOrDigit(value[0])) return false;
            foreach (char character in value) if (!IsNameCharacter(character)) return false;
            foreach (string segment in value.Split('/')) if (segment.Length == 0 || segment == "." || segment == "..") return false;
            normalized = value;
            return true;
        }

        /// <summary>Normalize tags to lowercase without duplicates, preserving their first order.</summary>
        /// <param name="tags">Tags to check; null means none.</param>
        /// <returns>The normalized tags.</returns>
        /// <exception cref="DomainException">400 invalid_resource_tags when a tag is invalid or there are too many.</exception>
        public static List<string> NormalizeTags(IEnumerable<string>? tags)
        {
            List<string> result = new List<string>();
            if (tags == null) return result;
            foreach (string tag in tags)
            {
                string value = (tag ?? "").Trim().ToLowerInvariant();
                if (value.Length == 0) continue;
                if (value.Length > MaxTagLength || !value.All(IsNameCharacter)) throw new DomainException(400, "invalid_resource_tags", "Tags use 1 to 64 characters: letters, digits, '-', '_', '.' and '/'.", "tags");
                if (!result.Contains(value, StringComparer.Ordinal)) result.Add(value);
            }
            if (result.Count > MaxTags) throw new DomainException(400, "invalid_resource_tags", "Use at most 16 tags.", "tags");
            return result;
        }

        /// <summary>Validate a title.</summary>
        /// <param name="title">Title text.</param>
        /// <exception cref="DomainException">400 invalid_resource_title when it is too long or spans lines.</exception>
        public static string Title(string? title)
        {
            string value = (title ?? "").Trim();
            if (value.Length > MaxTitleLength || value.Contains('\n', StringComparison.Ordinal) || value.Contains('\r', StringComparison.Ordinal) || value.Contains('\0', StringComparison.Ordinal)) throw new DomainException(400, "invalid_resource_title", "Use a single-line title of at most 200 characters.", "title");
            return value;
        }

        /// <summary>Validate a description.</summary>
        /// <param name="description">Description text.</param>
        /// <exception cref="DomainException">400 invalid_resource_description when it is too long or spans lines.</exception>
        public static string Description(string? description)
        {
            string value = (description ?? "").Trim();
            if (value.Length > MaxDescriptionLength || value.Contains('\n', StringComparison.Ordinal) || value.Contains('\r', StringComparison.Ordinal) || value.Contains('\0', StringComparison.Ordinal)) throw new DomainException(400, "invalid_resource_description", "Use a single-line description of at most 500 characters.", "description");
            return value;
        }

        /// <summary>Validate a body's size and characters.</summary>
        /// <param name="body">Markdown body.</param>
        /// <exception cref="DomainException">413 resource_too_large over 256 KiB, or 400 invalid_resource_body with a NUL character.</exception>
        public static string Body(string? body)
        {
            string value = body ?? "";
            if (value.Contains('\0', StringComparison.Ordinal)) throw new DomainException(400, "invalid_resource_body", "Resource text cannot contain NUL characters.", "body");
            if (Encoding.UTF8.GetByteCount(value) > MaxBodyBytes) throw new DomainException(413, "resource_too_large", "Resource text must fit 256 KiB (262144 UTF-8 bytes).", "body");
            return value;
        }

        /// <summary>Derive a resource name from a file name: the name without its extension, lowercased, with unsupported characters replaced by hyphens.</summary>
        /// <param name="fileName">File name or path.</param>
        /// <returns>A candidate name, which may still be invalid (for example when empty).</returns>
        public static string NameFromFileName(string fileName)
        {
            ArgumentNullException.ThrowIfNull(fileName);
            string name = fileName.Replace('\\', '/');
            int slash = name.LastIndexOf('/');
            if (slash >= 0) name = name.Substring(slash + 1);
            if (name.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 9);
            else if (name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 3);
            StringBuilder result = new StringBuilder();
            foreach (char character in name.Trim().ToLowerInvariant()) result.Append(IsNameCharacter(character) && character != '/' ? character : '-');
            string text = result.ToString().Trim('-', '.');
            return text.Length > MaxNameLength ? text.Substring(0, MaxNameLength) : text;
        }

        /// <summary>Human-readable size, for example 12.3 KiB.</summary>
        /// <param name="bytes">Size in bytes.</param>
        /// <returns>The formatted size.</returns>
        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            return (bytes / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + " KiB";
        }

        private static bool IsNameCharacter(char character)
        { return (character >= 'a' && character <= 'z') || (character >= '0' && character <= '9') || character == '-' || character == '_' || character == '.' || character == '/'; }
    }
}
