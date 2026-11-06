namespace ArgueBot.Cli
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;

    /// <summary>The resources command: list, show, add, update, rename, lock, delete, history, revert, import and export.</summary>
    internal static class ResourceCommands
    {
        internal const string Usage = "Use resources list [SEARCH] [--tag T], show NAME|ID [--revision N], add --file PATH|- [--name NAME], update NAME|ID --revision N --file PATH|-, rename NAME|ID NEW_NAME, lock|unlock NAME|ID, delete NAME|ID [--revision N], history NAME|ID, revert NAME|ID REVISION, import PATH... [--overwrite] or export NAME|ID [--output PATH].";

        internal static async Task<int> RunAsync(ArgueBotClient client, List<string> words, CancellationToken token)
        {
            string operation = words.Count > 1 ? words[1] : "list";
            List<string> rest = words.Skip(2).ToList();
            switch (operation)
            {
                case "list":
                    {
                        string tag = Option(rest, "--tag") ?? "";
                        Program.Print(await client.AllResourcesAsync(string.Join(" ", rest), tag, token).ConfigureAwait(false));
                        return 0;
                    }
                case "show":
                    {
                        string? revision = Option(rest, "--revision");
                        Resource resource = await client.ResourceAsync(Target(rest), token).ConfigureAwait(false);
                        if (revision == null) Program.Print(resource);
                        else Program.Print(await client.ResourceRevisionAsync(resource.Id, Number(revision), token).ConfigureAwait(false));
                        return 0;
                    }
                case "add":
                    {
                        string? name = Option(rest, "--name");
                        string? file = Option(rest, "--file");
                        bool stdin = rest.Remove("-");
                        if (file == null && !stdin) throw new ArgumentException("Use resources add --file PATH or resources add - with markdown on stdin.");
                        string content = file == null ? await Console.In.ReadToEndAsync(token).ConfigureAwait(false) : await File.ReadAllTextAsync(file, token).ConfigureAwait(false);
                        ResourceRequest parsed = ResourceMarkdown.Parse(file ?? (name ?? "") + ".md", content);
                        if (name != null) parsed.Name = name;
                        if (string.IsNullOrWhiteSpace(parsed.Name)) throw new ArgumentException("Give the resource a name with --name or a name: line in its front matter.");
                        Program.Print(await client.CreateResourceAsync(parsed, token).ConfigureAwait(false));
                        return 0;
                    }
                case "update":
                    {
                        string revision = Option(rest, "--revision") ?? throw new ArgumentException("Pass the current revision with --revision N. resources show prints it.");
                        string? file = Option(rest, "--file");
                        bool stdin = rest.Remove("-");
                        if (file == null && !stdin) throw new ArgumentException("Use --file PATH or - for markdown on stdin.");
                        Resource current = await client.ResourceAsync(Target(rest), token).ConfigureAwait(false);
                        string content = file == null ? await Console.In.ReadToEndAsync(token).ConfigureAwait(false) : await File.ReadAllTextAsync(file, token).ConfigureAwait(false);
                        ResourceRequest parsed = ResourceMarkdown.Parse(current.Name + ".md", content);
                        // Front matter may set the title, description, tags and lock; the name stays unless renamed explicitly.
                        parsed.Name = null; parsed.Revision = Number(revision);
                        Program.Print(await client.UpdateResourceAsync(current.Id, parsed, token).ConfigureAwait(false));
                        return 0;
                    }
                case "rename":
                    {
                        Program.Require(words, 4);
                        Resource current = await client.ResourceAsync(words[2], token).ConfigureAwait(false);
                        Program.Print(await client.UpdateResourceAsync(current.Id, new ResourceRequest { Name = words[3], Revision = current.Revision }, token).ConfigureAwait(false));
                        return 0;
                    }
                case "lock":
                case "unlock":
                    {
                        Resource current = await client.ResourceAsync(Target(rest), token).ConfigureAwait(false);
                        Program.Print(await client.UpdateResourceAsync(current.Id, new ResourceRequest { Locked = operation == "lock", Revision = current.Revision }, token).ConfigureAwait(false));
                        return 0;
                    }
                case "delete":
                    {
                        string? revision = Option(rest, "--revision");
                        Resource current = await client.ResourceAsync(Target(rest), token).ConfigureAwait(false);
                        Program.Print(await client.DeleteResourceAsync(current.Id, revision == null ? current.Revision : Number(revision), token).ConfigureAwait(false));
                        return 0;
                    }
                case "history":
                    {
                        Resource current = await client.ResourceAsync(Target(rest), token).ConfigureAwait(false);
                        Program.Print(await client.ResourceRevisionsAsync(current.Id, token).ConfigureAwait(false));
                        return 0;
                    }
                case "revert":
                    {
                        Program.Require(words, 4);
                        Resource current = await client.ResourceAsync(words[2], token).ConfigureAwait(false);
                        Program.Print(await client.RevertResourceAsync(current.Id, new ResourceRevertRequest { TargetRevision = Number(words[3]), Revision = current.Revision }, token).ConfigureAwait(false));
                        return 0;
                    }
                case "import":
                    {
                        bool overwrite = rest.Remove("--overwrite");
                        if (rest.Count == 0) throw new ArgumentException("Name one or more markdown files to import.");
                        List<ResourceImportFile> files = new List<ResourceImportFile>();
                        foreach (string path in rest) files.Add(new ResourceImportFile { FileName = Path.GetFileName(path), Content = await File.ReadAllTextAsync(path, token).ConfigureAwait(false) });
                        Program.Print(await client.ImportResourcesAsync(new ResourceImportRequest { Files = files, Overwrite = overwrite }, token).ConfigureAwait(false));
                        return 0;
                    }
                case "export":
                    {
                        string? output = Option(rest, "--output");
                        Resource current = await client.ResourceAsync(Target(rest), token).ConfigureAwait(false);
                        string markdown = await client.ExportResourceAsync(current.Id, token).ConfigureAwait(false);
                        if (output == null) Console.Write(markdown);
                        else { await File.WriteAllTextAsync(output, markdown, token).ConfigureAwait(false); Console.WriteLine("Exported " + current.Name + " to " + output); }
                        return 0;
                    }
                default: throw new ArgumentException(Usage);
            }
        }

        /// <summary>Remove an option and its value, returning the value, or null when absent.</summary>
        private static string? Option(List<string> words, string name)
        {
            int index = words.IndexOf(name);
            if (index < 0) return null;
            if (index + 1 >= words.Count) throw new ArgumentException(name + " needs a value.");
            string value = words[index + 1];
            words.RemoveRange(index, 2);
            return value;
        }

        private static string Target(List<string> rest)
        {
            if (rest.Count != 1) throw new ArgumentException("Name one resource by name or ID (res_...).");
            return rest[0];
        }

        private static long Number(string value) { return long.Parse(value, CultureInfo.InvariantCulture); }
    }
}
