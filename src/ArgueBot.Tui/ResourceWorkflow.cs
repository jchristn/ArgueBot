namespace ArgueBot.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using TUIKit.Hosting;

    /// <summary>The terminal Resources screen: list, read, edit in a text editor, rename, tag, lock, history with diff and revert, import, export and delete.</summary>
    internal sealed class ResourceWorkflow
    {
        private readonly ArgueBotClient _Client;
        private readonly TuiApplication _Application;
        private readonly UiDispatcher _Dispatcher;
        private readonly CancellationToken _Token;

        internal ResourceWorkflow(ArgueBotClient client, TuiApplication application, UiDispatcher dispatcher, CancellationToken token)
        { _Client = client; _Application = application; _Dispatcher = dispatcher; _Token = token; }

        private Task<T> Ui<T>(Func<Task<T>> action) { return _Dispatcher.InvokeAsync(action); }
        private Task<int> SelectAsync(string title, IEnumerable<string> choices) { return Ui(() => _Application.SelectStyledAsync(title, choices.ToArray())); }
        private Task<string?> PromptAsync(string title, string initial = "") { return Ui(() => _Application.PromptStyledAsync(title, initial)); }
        private Task<bool> ConfirmAsync(string message, string confirm) { return Ui(() => _Application.ConfirmStyledAsync(message, confirm, "Cancel")); }
        private async Task PanelAsync(string title, string content)
        {
            await Ui(async () =>
            {
                MessageDialog dialog = new MessageDialog(new Message { Speaker = "ArgueBot", Kind = title, Content = content }) { Title = title };
                dialog.ApplyTheme(_Application.Theme);
                await _Application.ShowAsync(dialog).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
        }
        private Task<string?> EditTextAsync(string title, string text)
        {
            return Ui(async () =>
            {
                TextEditorDialog dialog = new TextEditorDialog(title, text); dialog.ApplyTheme(_Application.Theme);
                return await _Application.ShowAsync<string>(dialog).ConfigureAwait(false);
            });
        }
        private Task<int> DiffAsync(string title, string oldText, string newText, params string[] actions)
        {
            return Ui(async () =>
            {
                DiffDialog dialog = new DiffDialog(title, oldText, newText, actions); dialog.ApplyTheme(_Application.Theme);
                object? result = await _Application.ShowAsync(dialog).ConfigureAwait(false);
                return result is int index ? index : -1;
            });
        }
        private static string Label(ResourceSummary item)
        {
            return item.Name + (item.Title.Length > 0 ? ": " + item.Title : "") + (item.Locked ? "  [locked]" : "") + (item.UpdatedByKind == "agent" ? "  [agent: " + item.UpdatedByName + "]" : "");
        }
        private static string Date(DateTime value) { return value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); }

        /// <summary>The resource list and its actions, until the user goes back.</summary>
        internal async Task RunAsync()
        {
            while (true)
            {
                List<ResourceSummary> resources = await _Client.AllResourcesAsync(token: _Token).ConfigureAwait(false);
                List<string> choices = new List<string> { "Add a resource", "Import markdown files" };
                choices.AddRange(resources.Select(Label));
                choices.Add("Back");
                int selected = await SelectAsync("Resources: markdown documents agents read with read_resource (" + resources.Count + ")", choices).ConfigureAwait(false);
                if (selected < 0 || selected == choices.Count - 1) return;
                try
                {
                    if (selected == 0) await AddAsync().ConfigureAwait(false);
                    else if (selected == 1) await ImportAsync().ConfigureAwait(false);
                    else await ResourceAsync(resources[selected - 2].Id).ConfigureAwait(false);
                }
                catch (DomainException exception) { await PanelAsync("Resource not changed", exception.Message).ConfigureAwait(false); }
                catch (IOException exception) { await PanelAsync("File not read", exception.Message).ConfigureAwait(false); }
            }
        }

        private async Task AddAsync()
        {
            string? name = await PromptAsync("Resource name (letters, digits, - _ . /; for example billing or team/conventions)").ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(name)) return;
            if (!ResourceRules.TryNormalizeName(name, out string normalized)) { await PanelAsync("Name not valid", "Use 1 to 128 characters: letters, digits, '-', '_', '.' and '/'. Start with a letter or digit, and do not use empty, '.' or '..' segments.").ConfigureAwait(false); return; }
            string? title = await PromptAsync("Title (optional)").ConfigureAwait(false); if (title == null) return;
            string? description = await PromptAsync("One-line description agents see in list_resources (optional)").ConfigureAwait(false); if (description == null) return;
            string? body = await EditTextAsync("Text for " + normalized + " (markdown)", "").ConfigureAwait(false); if (body == null) return;
            Resource created = await _Client.CreateResourceAsync(new ResourceRequest { Name = normalized, Title = title, Description = description, Body = body }, _Token).ConfigureAwait(false);
            await PanelAsync("Resource created", "Created " + created.Name + " at revision 1. In a prompt or role instruction, tell agents: if you need it, call read_resource with name " + created.Name + ".").ConfigureAwait(false);
        }

        private async Task ImportAsync()
        {
            string? paths = await PromptAsync("Markdown files to import (separate several paths with ;)").ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(paths)) return;
            List<ResourceImportFile> files = new List<ResourceImportFile>();
            foreach (string path in paths.Split(';').Select(item => item.Trim().Trim('"')).Where(item => item.Length > 0))
                files.Add(new ResourceImportFile { FileName = Path.GetFileName(path), Content = await File.ReadAllTextAsync(path, _Token).ConfigureAwait(false) });
            ResourceImportResult result;
            try { result = await _Client.ImportResourcesAsync(new ResourceImportRequest { Files = files }, _Token).ConfigureAwait(false); }
            catch (DomainException exception) when (exception.Code == "resource_exists")
            {
                if (!await ConfirmAsync(exception.Message + " Replace existing resources with the imported text? Their history is kept.", "Replace").ConfigureAwait(false)) return;
                result = await _Client.ImportResourcesAsync(new ResourceImportRequest { Files = files, Overwrite = true }, _Token).ConfigureAwait(false);
            }
            await PanelAsync("Imported", result.Created + " new and " + result.Updated + " replaced:\n\n" + string.Join("\n", result.Items.Select(item => "- " + item.Name))).ConfigureAwait(false);
        }

        private async Task ResourceAsync(string id)
        {
            while (true)
            {
                Resource resource = await _Client.ResourceAsync(id, _Token).ConfigureAwait(false);
                string[] actions = { "Read", "Edit text", "Edit name, title, description and tags", resource.Locked ? "Unlock: let agents change it" : "Lock: agents can read but not change it", "History, changes and revert", "Export to a markdown file", "Delete", "Back" };
                string heading = resource.Name + " (revision " + resource.Revision + ", " + ResourceRules.FormatSize(resource.Bytes) + (resource.UpdatedByKind == "agent" ? ", last written by agent " + resource.UpdatedByName : "") + ")";
                int action = await SelectAsync(heading, actions).ConfigureAwait(false);
                if (action < 0 || action == actions.Length - 1) return;
                try
                {
                    switch (action)
                    {
                        case 0: await PanelAsync(resource.Name, Describe(resource) + "\n\n" + resource.Body).ConfigureAwait(false); break;
                        case 1:
                            {
                                string? body = await EditTextAsync("Edit " + resource.Name + " (revision " + resource.Revision + ")", resource.Body).ConfigureAwait(false);
                                if (body == null || body == resource.Body) break;
                                await _Client.UpdateResourceAsync(resource.Id, new ResourceRequest { Body = body, Revision = resource.Revision }, _Token).ConfigureAwait(false);
                                break;
                            }
                        case 2:
                            {
                                string? name = await PromptAsync("Name", resource.Name).ConfigureAwait(false); if (name == null) break;
                                string? title = await PromptAsync("Title", resource.Title).ConfigureAwait(false); if (title == null) break;
                                string? description = await PromptAsync("Description", resource.Description).ConfigureAwait(false); if (description == null) break;
                                string? tags = await PromptAsync("Tags, separated by commas", string.Join(", ", resource.Tags)).ConfigureAwait(false); if (tags == null) break;
                                await _Client.UpdateResourceAsync(resource.Id, new ResourceRequest { Name = name, Title = title, Description = description, Tags = tags.Split(',').Select(tag => tag.Trim()).Where(tag => tag.Length > 0).ToList(), Revision = resource.Revision }, _Token).ConfigureAwait(false);
                                break;
                            }
                        case 3: await _Client.UpdateResourceAsync(resource.Id, new ResourceRequest { Locked = !resource.Locked, Revision = resource.Revision }, _Token).ConfigureAwait(false); break;
                        case 4: await HistoryAsync(resource).ConfigureAwait(false); break;
                        case 5:
                            {
                                string? path = await PromptAsync("Save as", Path.Combine(Environment.CurrentDirectory, resource.Name.Replace('/', '_') + ".md")).ConfigureAwait(false);
                                if (string.IsNullOrWhiteSpace(path)) break;
                                await File.WriteAllTextAsync(path, await _Client.ExportResourceAsync(resource.Id, _Token).ConfigureAwait(false), new UTF8Encoding(false), _Token).ConfigureAwait(false);
                                await PanelAsync("Exported", "Saved " + resource.Name + " to " + path + ".").ConfigureAwait(false);
                                break;
                            }
                        case 6:
                            if (!await ConfirmAsync("Delete " + resource.Name + " and its history? Debates that refer to it will no longer find it.", "Delete").ConfigureAwait(false)) break;
                            await _Client.DeleteResourceAsync(resource.Id, resource.Revision, _Token).ConfigureAwait(false);
                            return;
                    }
                }
                catch (DomainException exception) when (exception.Code == "revision_conflict") { await PanelAsync("Changed elsewhere", exception.Message + " Your change was not saved; open the resource again to see the latest text.").ConfigureAwait(false); }
                catch (DomainException exception) { await PanelAsync("Resource not changed", exception.Message).ConfigureAwait(false); }
                catch (IOException exception) { await PanelAsync("File not saved", exception.Message).ConfigureAwait(false); }
            }
        }

        private async Task HistoryAsync(Resource resource)
        {
            List<ResourceRevision> revisions = await _Client.ResourceRevisionsAsync(resource.Id, _Token).ConfigureAwait(false);
            List<string> choices = revisions.Select(item => "Revision " + item.Revision + ": " + item.Change + " by " + (item.AuthorKind == "agent" ? "agent " : "") + item.AuthorName + ", " + Date(item.CreatedUtc)).Concat(new[] { "Back" }).ToList();
            int selected = await SelectAsync("History of " + resource.Name + " (newest first)", choices).ConfigureAwait(false);
            if (selected < 0 || selected >= revisions.Count) return;
            ResourceRevision revision = await _Client.ResourceRevisionAsync(resource.Id, revisions[selected].Revision, _Token).ConfigureAwait(false);
            if (revision.Revision == resource.Revision) { await PanelAsync("Revision " + revision.Revision, "This is the current revision.\n\n" + revision.Body).ConfigureAwait(false); return; }
            int action = await DiffAsync("Changes from revision " + revision.Revision + " to the current revision " + resource.Revision, revision.Body, resource.Body, "Restore revision " + revision.Revision).ConfigureAwait(false);
            if (action != 0) return;
            Resource restored = await _Client.RevertResourceAsync(resource.Id, new ResourceRevertRequest { TargetRevision = revision.Revision, Revision = resource.Revision }, _Token).ConfigureAwait(false);
            await PanelAsync("Restored", "Saved revision " + revision.Revision + "'s text as revision " + restored.Revision + ".").ConfigureAwait(false);
        }

        private static string Describe(Resource resource)
        {
            StringBuilder text = new StringBuilder();
            if (resource.Title.Length > 0) text.Append("Title: ").Append(resource.Title).Append('\n');
            if (resource.Description.Length > 0) text.Append("Description: ").Append(resource.Description).Append('\n');
            if (resource.Tags.Count > 0) text.Append("Tags: ").Append(string.Join(", ", resource.Tags)).Append('\n');
            text.Append("Revision ").Append(resource.Revision).Append(", updated ").Append(Date(resource.UpdatedUtc)).Append(" by ").Append(resource.UpdatedByKind == "agent" ? "agent " : "").Append(resource.UpdatedByName);
            if (resource.UpdatedByKind == "agent") text.Append("\nAn agent wrote the latest revision. Review it before relying on it.");
            if (resource.Locked) text.Append("\nLocked: agents can read it but not change it.");
            return text.ToString();
        }
    }
}
