namespace ArgueBot.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Encodings.Web;
    using System.Text.Json;
    using ArgueBot.Core.Models;

    /// <summary>Reads and writes resources as markdown files with optional front matter (name, title, description, tags, locked) between --- lines.</summary>
    public static class ResourceMarkdown
    {
        private static readonly JsonSerializerOptions _Quoted = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        /// <summary>Parse a markdown file. Values missing from the front matter stay null, except the name, which falls back to the file name.</summary>
        /// <param name="fileName">Original file name.</param>
        /// <param name="content">File text.</param>
        /// <returns>A create request holding the parsed fields and body.</returns>
        /// <exception cref="DomainException">400 invalid_front_matter when the front matter is not closed.</exception>
        public static ResourceRequest Parse(string fileName, string content)
        {
            ArgumentNullException.ThrowIfNull(fileName); ArgumentNullException.ThrowIfNull(content);
            ResourceRequest request = new ResourceRequest { Body = content };
            string text = content.StartsWith('﻿') ? content.Substring(1) : content;
            List<string> lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
            if (lines.Count > 0 && lines[0].TrimEnd() == "---")
            {
                int end = lines.FindIndex(1, line => line.TrimEnd() == "---" || line.TrimEnd() == "...");
                if (end < 0) throw new DomainException(400, "invalid_front_matter", "The front matter in " + fileName + " starts with --- but has no closing --- line.", "files");
                string? listKey = null; List<string> tags = new List<string>();
                for (int index = 1; index < end; index++)
                {
                    string line = lines[index];
                    if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#')) continue;
                    if (listKey != null && line.TrimStart().StartsWith("- ", StringComparison.Ordinal)) { if (listKey == "tags") tags.Add(Scalar(line.TrimStart().Substring(2))); continue; }
                    listKey = null;
                    int colon = line.IndexOf(':', StringComparison.Ordinal);
                    if (colon <= 0) continue;
                    string key = line.Substring(0, colon).Trim().ToLowerInvariant();
                    string value = line.Substring(colon + 1).Trim();
                    switch (key)
                    {
                        case "name": request.Name = Scalar(value); break;
                        case "title": request.Title = Scalar(value); break;
                        case "description": request.Description = Scalar(value); break;
                        case "locked": request.Locked = string.Equals(Scalar(value), "true", StringComparison.OrdinalIgnoreCase) || Scalar(value) == "yes"; break;
                        case "tags":
                            if (value.Length == 0) { listKey = "tags"; break; }
                            string inner = value.StartsWith('[') && value.EndsWith(']') ? value.Substring(1, value.Length - 2) : value;
                            tags.AddRange(inner.Split(',').Select(Scalar).Where(tag => tag.Length > 0));
                            break;
                    }
                }
                if (tags.Count > 0) request.Tags = tags;
                int bodyStart = end + 1;
                if (bodyStart < lines.Count && lines[bodyStart].Trim().Length == 0) bodyStart++;
                request.Body = string.Join("\n", lines.Skip(bodyStart));
            }
            if (string.IsNullOrWhiteSpace(request.Name)) request.Name = ResourceRules.NameFromFileName(fileName);
            return request;
        }

        /// <summary>Write a resource as markdown with front matter that Parse reads back.</summary>
        /// <param name="resource">Resource to export.</param>
        /// <returns>Markdown text.</returns>
        public static string Format(Resource resource)
        {
            ArgumentNullException.ThrowIfNull(resource);
            StringBuilder text = new StringBuilder("---\n");
            text.Append("name: ").Append(resource.Name).Append('\n');
            if (resource.Title.Length > 0) text.Append("title: ").Append(JsonSerializer.Serialize(resource.Title, _Quoted)).Append('\n');
            if (resource.Description.Length > 0) text.Append("description: ").Append(JsonSerializer.Serialize(resource.Description, _Quoted)).Append('\n');
            if (resource.Tags.Count > 0) text.Append("tags: [").Append(string.Join(", ", resource.Tags)).Append("]\n");
            if (resource.Locked) text.Append("locked: true\n");
            text.Append("---\n\n").Append(resource.Body);
            return text.ToString();
        }

        private static string Scalar(string value)
        {
            string text = value.Trim();
            if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
            {
                try { return JsonSerializer.Deserialize<string>(text) ?? ""; }
                catch (JsonException) { return text.Substring(1, text.Length - 2); }
            }
            if (text.Length >= 2 && text[0] == '\'' && text[text.Length - 1] == '\'') return text.Substring(1, text.Length - 2).Replace("''", "'", StringComparison.Ordinal);
            return text;
        }
    }
}
