namespace ArgueBot.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Models;
    using TUIKit.Hosting;

    internal sealed class RecoveryWorkflow
    {
        private readonly ArgueBotClient _Client;
        private readonly TuiApplication _Application;
        private readonly UiDispatcher _Dispatcher;
        private readonly CancellationToken _Token;
        internal RecoveryWorkflow(ArgueBotClient client, TuiApplication application, UiDispatcher dispatcher, CancellationToken token)
        { _Client = client; _Application = application; _Dispatcher = dispatcher; _Token = token; }
        internal async Task RunAsync()
        {
            RequestContext principal = await _Client.SendAsync<RequestContext>(HttpMethod.Get, "v1.0/api/auth/me", token: _Token).ConfigureAwait(false);
            if (!principal.IsAdmin) throw new InvalidOperationException("Complete-service recovery requires a system administrator.");
            int action = await _Dispatcher.InvokeAsync(() => _Application.SelectAsync("Recovery on the service machine", "Create recovery archive", "Browse archives", "Browse durable operations")).ConfigureAwait(false);
            if (action == 0) await SubmitAsync("create", null).ConfigureAwait(false);
            else if (action == 1) await ArchivesAsync().ConfigureAwait(false);
            else if (action == 2) await OperationsAsync("").ConfigureAwait(false);
        }
        private async Task SubmitAsync(string kind, BackupEntry? entry)
        {
            BackupOperation? accepted = null;
            BackupRequest? request = await _Dispatcher.InvokeAsync(async () =>
            {
                RecoveryEditor editor = new RecoveryEditor(kind, entry?.Encrypted ?? false);
                FormDialog<BackupRequest> dialog = new FormDialog<BackupRequest>(kind + " recovery archive", editor.Form, editor.Extract, _Application, async value =>
                {
                    string path = "v1.0/api/backups" + (entry == null ? "" : "/" + Uri.EscapeDataString(entry.Id) + "/" + kind);
                    accepted = await _Client.SendAsync<BackupOperation>(HttpMethod.Post, path, value, _Token).ConfigureAwait(false);
                });
                return await _Application.ShowAsync<BackupRequest>(dialog).ConfigureAwait(false);
            }).ConfigureAwait(false);
            if (request == null || accepted == null) return;
            request.Password = "";
            await _Dispatcher.InvokeAsync(() => _Application.ConfirmAsync("Accepted " + accepted.Kind + " operation " + accepted.Id + ". This is acceptance, not completion. Browse durable operations to refresh its status; it continues after you detach.", "Close", "Close")).ConfigureAwait(false);
        }
        private async Task ArchivesAsync()
        {
            int page = 1;
            while (true)
            {
                BackupPage result = await _Client.SendAsync<BackupPage>(HttpMethod.Get, "v1.0/api/backups?pageNumber=" + page, token: _Token).ConfigureAwait(false);
                List<string> choices = result.Items.Select(entry => entry.Name + " | " + entry.Status + (entry.Encrypted ? " | encrypted" : "")).ToList();
                int previous = -1; int next = -1;
                if (page > 1) { previous = choices.Count; choices.Add("Previous page"); }
                if ((long)page * result.PageSize < result.TotalCount) { next = choices.Count; choices.Add("Next page"); }
                if (choices.Count == 0) { await _Dispatcher.InvokeAsync(() => _Application.ConfirmAsync("No recovery archives yet.", "Close", "Close")).ConfigureAwait(false); return; }
                int selected = await _Dispatcher.InvokeAsync(() => _Application.SelectAsync("Recovery archives | page " + page + " | " + result.TotalCount + " total", choices.ToArray())).ConfigureAwait(false);
                if (selected < 0) return;
                if (selected == previous) { page--; continue; }
                if (selected == next) { page++; continue; }
                BackupEntry entry = result.Items[selected];
                string[] actions = entry.Status == "ready" ? new[] { "Verify archive", "Restore into a new SQLite home", "Download onto this client machine", "View artifact operations", "Delete idle archive" } : new[] { "View artifact operations", "Delete idle archive" };
                int action = await _Dispatcher.InvokeAsync(() => _Application.SelectAsync(entry.Name + " | " + entry.Bytes + " bytes", actions)).ConfigureAwait(false);
                if (action >= 0 && entry.Status != "ready") action += 3;
                if (action == 0 || action == 1) await SubmitAsync(action == 0 ? "verify" : "restore", entry).ConfigureAwait(false);
                else if (action == 2)
                {
                    string? destination = await _Dispatcher.InvokeAsync(() => _Application.PromptAsync("New download file on this client machine (existing files are refused)")).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(destination)) await _Client.DownloadBackupAsync(entry.Id, destination, _Token).ConfigureAwait(false);
                }
                else if (action == 3) await OperationsAsync(entry.Id).ConfigureAwait(false);
                else if (action == 4 && await _Dispatcher.InvokeAsync(() => _Application.ConfirmAsync("Permanently delete " + entry.Name + " and its operation records? Conversation data is unchanged.")).ConfigureAwait(false))
                    await _Client.SendAsync<DeletionResponse>(HttpMethod.Delete, "v1.0/api/backups/" + Uri.EscapeDataString(entry.Id), token: _Token).ConfigureAwait(false);
            }
        }
        private async Task OperationsAsync(string backupId)
        {
            int page = 1;
            while (true)
            {
                BackupOperationPage result = await _Client.SendAsync<BackupOperationPage>(HttpMethod.Get, "v1.0/api/backup-operations?backupId=" + Uri.EscapeDataString(backupId) + "&pageNumber=" + page, token: _Token).ConfigureAwait(false);
                List<string> choices = result.Items.Select(operation => operation.Kind + " | " + operation.State + " | " + operation.Id).ToList();
                int previous = -1; int next = -1;
                if (page > 1) { previous = choices.Count; choices.Add("Previous page"); }
                if ((long)page * result.PageSize < result.TotalCount) { next = choices.Count; choices.Add("Next page"); }
                if (choices.Count == 0) { await _Dispatcher.InvokeAsync(() => _Application.ConfirmAsync("No recovery operations.", "Close", "Close")).ConfigureAwait(false); return; }
                int selected = await _Dispatcher.InvokeAsync(() => _Application.SelectAsync("Durable operations | page " + page, choices.ToArray())).ConfigureAwait(false);
                if (selected < 0) return;
                if (selected == previous) { page--; continue; }
                if (selected == next) { page++; continue; }
                string id = result.Items[selected].Id;
                bool refresh;
                do
                {
                    BackupOperation operation = await _Client.SendAsync<BackupOperation>(HttpMethod.Get, "v1.0/api/backup-operations/" + Uri.EscapeDataString(id), token: _Token).ConfigureAwait(false);
                    string text = operation.Kind + " | " + operation.State + " | " + operation.Id + "\n" + operation.ErrorCode + "\n" + operation.TargetHome + "\nMissing credential references: " + string.Join(", ", operation.MissingReferences);
                    refresh = await _Dispatcher.InvokeAsync(() => _Application.ConfirmAsync(text, "Refresh", "Back")).ConfigureAwait(false);
                } while (refresh);
            }
        }
    }
}
