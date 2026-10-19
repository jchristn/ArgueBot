#if NET10_0_OR_GREATER
namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Models;
    using ArgueBot.Tui;
    using TUIKit;
    using TUIKit.Hosting;
    using TUIKit.Terminal;
    using TUIKit.Testing;
    using TUIKit.Theming;
    using TUIKit.Widgets;
    using Touchstone.Core;

    /// <summary>Headless tests render and drive the actual terminal application shell.</summary>
    public static class TerminalSuites
    {
        /// <summary>Terminal descriptors run on the executable's .NET 10 target.</summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get { return new List<TestSuiteDescriptor> { new TestSuiteDescriptor("Terminal", "Headless terminal", new List<TestCaseDescriptor> { new TestCaseDescriptor("Terminal", "UserForms", "User forms mask passwords, preserve administrative boundaries and validate drafts", UserFormsAsync), new TestCaseDescriptor("Terminal", "MessageReader", "Full-message reader is read-only, scrolls long Unicode text and keeps a close action visible", MessageReaderAsync), new TestCaseDescriptor("Terminal", "AttemptDetails", "Attempt inspector preserves ordered sources and scrolls through read-only provenance", AttemptDetailsAsync), new TestCaseDescriptor("Terminal", "AuditDetails", "Audit detail remains read-only through paste and scrolls to its close actions", AuditDetailsAsync), new TestCaseDescriptor("Terminal", "RecoveryForms", "Recovery forms mask passphrases and reject unsafe restore destinations", RecoveryFormsAsync), new TestCaseDescriptor("Terminal", "ParticipantForms", "Participant forms preserve empty arguments, validate required fields and fit terminal sizes", ParticipantFormsAsync), new TestCaseDescriptor("Terminal", "Shell", "Responsive shell renders, traverses focus and toggles theme", ShellAsync) }) }; }
        }
        private static async Task UserFormsAsync(CancellationToken token)
        {
            foreach (Theme theme in new[] { Theme.Dark, Theme.Light })
            {
                HeadlessBackend backend = new HeadlessBackend(60, 24) { ClaimsTerminal = false };
                using (TuiApplication app = new TuiApplication(backend))
                {
                    app.Theme = theme; app.Start();
                    UserEditor editor = new UserEditor(false, new UserRecord { Name = "Terminal identity", Email = "terminal@example.invalid", Role = "viewer", Active = true, Revision = 7 });
                    FormDialog<UserRequest> dialog = new FormDialog<UserRequest>("Edit tenant user", editor.Form, editor.Extract); dialog.ApplyTheme(theme);
                    Task<UserRequest?> result = app.ShowAsync<UserRequest>(dialog);
                    if (editor.Form.FieldCount != 6) throw new InvalidDataException("Tenant user editor exposed the system administrator flag.");
                    ((TextField)editor.Form.GetField(1)).Value = "invalid email";
                    backend.FeedKey("ctrl+s"); app.PumpInputOnce();
                    if (dialog.IsClosed) throw new InvalidDataException("Invalid identity metadata closed its draft.");
                    ((TextField)editor.Form.GetField(1)).Value = "terminal@example.invalid";
                    ((TextField)editor.Form.GetField(5)).Value = "fictional-terminal-password";
                    editor.Form.SetFocusedField(5); app.RenderOnce();
                    string screen = Snapshot.ToText(app.CaptureFrame()!);
                    if (screen.Contains("fictional-terminal-password", StringComparison.Ordinal)) throw new InvalidDataException("The terminal rendered an unmasked login password.");
                    await SaveEvidenceAsync("user-editor-" + theme.Name, screen, token).ConfigureAwait(false);
                    backend.FeedKey("ctrl+s"); app.PumpInputOnce();
                    UserRequest? request = await result.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                    if (request == null || request.PasswordSha256?.Length != 64 || request.IsAdmin || request.ExpectedRevision != 7 || request.Email != "terminal@example.invalid") throw new InvalidDataException("The terminal identity form changed privileges, revision or password representation.");
                    app.Stop();
                }
            }
        }
        private static async Task MessageReaderAsync(CancellationToken token)
        {
            foreach (Theme theme in new[] { Theme.Light, Theme.Dark })
            {
                HeadlessBackend backend = new HeadlessBackend(60, 24) { ClaimsTerminal = false };
                using (TuiApplication app = new TuiApplication(backend))
                {
                    app.Theme = theme; app.Start();
                    string text = string.Join("\n", Enumerable.Range(1, 300).Select(index => "Read-only Unicode line 議論🙂 " + index)) + "\nCOMPLETE MESSAGE TAIL";
                    MessageDialog dialog = new MessageDialog(new Message { Content = text });
                    Task<Message?> result = app.ShowAsync<Message>(dialog);
                    dialog.HandlePaste("attempted mutation");
                    backend.FeedKey("ctrl+end"); app.PumpInputOnce(); app.RenderOnce();
                    string screen = Snapshot.ToText(app.CaptureFrame()!);
                    if (dialog.Content != text || !screen.Contains("COMPLETE MESSAGE TAIL", StringComparison.Ordinal) || !screen.Contains("Close", StringComparison.Ordinal)) throw new InvalidDataException("A long full message became editable, lost its tail or hid the close action.");
                    await SaveEvidenceAsync("message-reader-" + theme.Name, screen, token).ConfigureAwait(false);
                    backend.FeedKey("tab"); backend.FeedKey("enter"); app.PumpInputOnce(); app.PumpInputOnce();
                    if (await result.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false) != null) throw new InvalidDataException("Closing the reader returned a mutation.");
                    app.Stop();
                }
            }
        }
        private static async Task AttemptDetailsAsync(CancellationToken token)
        {
            foreach (Theme theme in new[] { Theme.Light, Theme.Dark })
            {
                HeadlessBackend backend = new HeadlessBackend(60, 24) { ClaimsTerminal = false };
                using (TuiApplication app = new TuiApplication(backend))
                {
                    app.Theme = theme; app.Start();
                    GenerationAttempt attempt = new GenerationAttempt { Id = "att_fixture", PromptVersion = "debate-v1", PromptDigest = new string('a', 64), State = "interrupted", ContextMessageIds = Enumerable.Range(1, 40).Select(index => "msg_fixture_" + index).ToList() };
                    AttemptDetails details = new AttemptDetails(attempt, new DebateRun { Question = "Immutable question 議論" });
                    FormDialog<GenerationAttempt> dialog = new FormDialog<GenerationAttempt>("Generation provenance", details.Form, () => attempt, readOnly: true);
                    Task<GenerationAttempt?> result = app.ShowAsync<GenerationAttempt>(dialog);
                    dialog.HandlePaste("attempted edit");
                    if (((TextField)details.Form.GetField(0)).Value != attempt.Id || ((TextField)details.Form.GetField(9)).Value != "msg_fixture_1" || ((TextField)details.Form.GetField(details.Form.FieldCount - 1)).Value != "msg_fixture_40") throw new InvalidDataException("Attempt provenance became editable or lost source ordering.");
                    for (int index = 0; index < details.Form.FieldCount + 1; index++) { backend.FeedKey("tab"); app.PumpInputOnce(); }
                    app.RenderOnce(); string screen = Snapshot.ToText(app.CaptureFrame()!);
                    if (!screen.Contains("msg_fixture_40", StringComparison.Ordinal) || !screen.Contains("Close", StringComparison.Ordinal)) throw new InvalidDataException("Long provenance did not scroll to its final source and close controls.");
                    await SaveEvidenceAsync("attempt-" + theme.Name, screen, token).ConfigureAwait(false);
                    backend.FeedKey("escape"); app.PumpInputOnce(); app.PumpInputOnce();
                    if (await result.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false) != null) throw new InvalidDataException("Read-only provenance inspection returned a mutation.");
                    app.Stop();
                }
            }
        }
        private static async Task AuditDetailsAsync(CancellationToken token)
        {
            foreach (Theme theme in new[] { Theme.Light, Theme.Dark })
            {
                HeadlessBackend backend = new HeadlessBackend(60, 24) { ClaimsTerminal = false };
                using (TuiApplication app = new TuiApplication(backend))
                {
                    app.Theme = theme; app.Start();
                    AuditEntry entry = new AuditEntry { Id = "aud_fixture", Operation = "session.issue", Outcome = "issued", TenantId = "ten_local", UserId = "usr_local", SessionId = "ses_fixture" };
                    AuditDetails details = new AuditDetails(entry);
                    FormDialog<AuditEntry> dialog = new FormDialog<AuditEntry>("Immutable security event", details.Form, () => entry, readOnly: true);
                    Task<AuditEntry?> result = app.ShowAsync<AuditEntry>(dialog);
                    dialog.HandlePaste("attempted edit");
                    if (((TextField)details.Form.GetField(0)).Value != entry.Id || !((TextField)details.Form.GetField(0)).IsReadOnly) throw new InvalidDataException("Security accounting could be edited in its inspector.");
                    for (int index = 0; index < details.Form.FieldCount + 1; index++) { backend.FeedKey("tab"); app.PumpInputOnce(); }
                    app.RenderOnce();
                    string screen = Snapshot.ToText(app.CaptureFrame()!);
                    if (!screen.Contains("Close", StringComparison.Ordinal) || !screen.Contains("Back", StringComparison.Ordinal) || screen.Contains("Save", StringComparison.Ordinal)) throw new InvalidDataException("Read-only inspection did not expose clear close actions.");
                    await SaveEvidenceAsync("audit-" + theme.Name, screen, token).ConfigureAwait(false);
                    backend.FeedKey("escape"); app.PumpInputOnce(); app.PumpInputOnce();
                    if (await result.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false) != null) throw new InvalidDataException("Closing read-only inspection returned a mutation.");
                    app.Stop();
                }
            }
        }
        private static async Task SaveEvidenceAsync(string name, string content, CancellationToken token)
        {
            string? folder = Environment.GetEnvironmentVariable("ARGUEBOT_TUI_EVIDENCE_DIR");
            if (string.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, name + ".txt"), content, token).ConfigureAwait(false);
        }
        private static async Task RecoveryFormsAsync(CancellationToken token)
        {
            foreach (Theme theme in new[] { Theme.Light, Theme.Dark })
            {
                HeadlessBackend backend = new HeadlessBackend(70, 24) { ClaimsTerminal = false };
                using (TuiApplication app = new TuiApplication(backend))
                {
                    app.Theme = theme; app.Start();
                    RecoveryEditor editor = new RecoveryEditor("restore", true);
                    FormDialog<BackupRequest> dialog = new FormDialog<BackupRequest>("Restore archive", editor.Form, editor.Extract);
                    Task<BackupRequest?> result = app.ShowAsync<BackupRequest>(dialog);
                    ((TextField)editor.Form.GetField(0)).Value = "fixture-recovery-passphrase";
                    ((TextField)editor.Form.GetField(1)).Value = "../unsafe-home";
                    backend.FeedKey("ctrl+s"); app.PumpInputOnce(); app.RenderOnce();
                    if (dialog.IsClosed) throw new InvalidDataException("The restore form accepted a traversing destination.");
                    string screen = Snapshot.ToText(app.CaptureFrame()!);
                    if (screen.Contains("fixture-recovery-passphrase", StringComparison.Ordinal)) throw new InvalidDataException("The recovery form rendered a plaintext passphrase.");
                    await SaveEvidenceAsync("recovery-" + theme.Name, screen, token).ConfigureAwait(false);
                    ((TextField)editor.Form.GetField(1)).Value = "recovered_fixture";
                    backend.FeedKey("ctrl+s"); app.PumpInputOnce();
                    BackupRequest? request = await result.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                    if (request?.DestinationName != "recovered_fixture" || request.Password != "fixture-recovery-passphrase" || request.Port != 4537) throw new InvalidDataException("The recovery form changed its explicit destination, passphrase or port.");
                    RecoveryEditor postgresql = new RecoveryEditor("restore");
                    ((TextField)postgresql.Form.GetField(1)).Value = "postgresql_fixture";
                    ((Dropdown<string>)postgresql.Form.GetField(3)).SelectedIndex = 1;
                    FormDialog<BackupRequest> providerDialog = new FormDialog<BackupRequest>("PostgreSQL restore", postgresql.Form, postgresql.Extract);
                    Task<BackupRequest?> providerResult = app.ShowAsync<BackupRequest>(providerDialog);
                    backend.FeedKey("ctrl+s"); app.PumpInputOnce();
                    if (providerDialog.IsClosed) throw new InvalidDataException("PostgreSQL restore accepted a missing connection reference.");
                    ((TextField)postgresql.Form.GetField(4)).Value = "ARGUEBOT_FIXTURE_RECOVERY_DATABASE";
                    backend.FeedKey("ctrl+s"); app.PumpInputOnce();
                    BackupRequest? providerRequest = await providerResult.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                    if (providerRequest?.RestoreDatabaseType != ArgueBot.Core.Database.DatabaseTypeEnum.Postgresql || providerRequest.RestoreConnectionEnvironmentVariable != "ARGUEBOT_FIXTURE_RECOVERY_DATABASE") throw new InvalidDataException("Terminal recovery lost the explicit PostgreSQL destination reference.");
                    app.Stop();
                }
            }
        }
        private static async Task ParticipantFormsAsync(CancellationToken token)
        {
            foreach (int width in new int[] { 60, 100 })
            foreach (Theme theme in new Theme[] { Theme.Dark, Theme.Light })
            {
                token.ThrowIfCancellationRequested();
                HeadlessBackend backend = new HeadlessBackend(width, 24) { ClaimsTerminal = false };
                using (TuiApplication app = new TuiApplication(backend))
                {
                    app.Theme = theme; app.Start();
                    RunnerProfile original = new RunnerProfile { Name = "Terminal 議論", Kind = "cli", Provider = "plain", Executable = "fixture", Arguments = new List<string> { "", "--directory", "space and Unicode 議論" } };
                    ParticipantEditor editor = new ParticipantEditor(original);
                    FormDialog<RunnerProfile> dialog = new FormDialog<RunnerProfile>("Edit participant", editor.Form, editor.Extract);
                    dialog.ApplyTheme(theme);
                    Task<RunnerProfile?> outcome = app.ShowAsync<RunnerProfile>(dialog);
                    app.RenderOnce();
                    string screen = Snapshot.ToText(app.CaptureFrame() ?? throw new InvalidDataException("Participant dialog frame was missing."));
                    await SaveEvidenceAsync("participants-" + width + "-" + theme.Name, screen, token).ConfigureAwait(false);
                    if (!screen.Contains("Edit participant", StringComparison.Ordinal) || !screen.Contains("Name", StringComparison.Ordinal)) throw new InvalidDataException("The participant form did not render its title and fields.");
                    editor.Form.SetFocusedField(editor.Form.FieldCount - 1);
                    backend.FeedKey("tab"); app.PumpInputOnce(); app.RenderOnce();
                    string bottom = Snapshot.ToText(app.CaptureFrame()!);
                    if (!bottom.Contains("Save", StringComparison.Ordinal) || !bottom.Contains("Cancel", StringComparison.Ordinal)) throw new InvalidDataException("Tab could not scroll the participant form's action buttons into view.");
                    await SaveEvidenceAsync("participants-bottom-" + width + "-" + theme.Name, bottom, token).ConfigureAwait(false);
                    backend.FeedKey("ctrl+s"); app.PumpInputOnce();
                    if (!dialog.IsClosed) throw new InvalidDataException("Save did not close the valid participant form: " + string.Join("; ", Enumerable.Range(0, editor.Form.FieldCount).Select(index => editor.Form.GetFieldError(index)).Where(error => error != null)));
                    RunnerProfile? submitted = await outcome.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                    if (submitted == null || !submitted.Arguments.SequenceEqual(original.Arguments)) throw new InvalidDataException("Terminal form submission changed literal argument order or dropped an empty argument.");
                    ParticipantEditor retained = new ParticipantEditor(original); int attempts = 0;
                    FormDialog<RunnerProfile> retry = new FormDialog<RunnerProfile>("Retry participant", retained.Form, retained.Extract, app, candidate =>
                    {
                        attempts++;
                        if (attempts == 1) throw new InvalidOperationException("Service rejected the fixture; edit and retry.");
                        return Task.CompletedTask;
                    });
                    Task<RunnerProfile?> saved = app.ShowAsync<RunnerProfile>(retry);
                    backend.FeedKey("ctrl+s"); app.PumpInputOnce(); app.PumpInputOnce(); app.RenderOnce();
                    if (retry.IsClosed || !retained.Extract().Arguments.SequenceEqual(original.Arguments)) throw new InvalidDataException("A service save failure closed the form or discarded its draft.");
                    string rejected = Snapshot.ToText(app.CaptureFrame()!);
                    if (!rejected.Contains("Service rejected", StringComparison.Ordinal)) throw new InvalidDataException("The form did not display its service failure inline.");
                    backend.FeedKey("ctrl+s"); app.PumpInputOnce(); app.PumpInputOnce();
                    await saved.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                    if (attempts != 2) throw new InvalidDataException("Retry did not submit the retained participant draft exactly once.");
                    ParticipantEditor blank = new ParticipantEditor();
                    FormDialog<RunnerProfile> invalid = new FormDialog<RunnerProfile>("Create participant", blank.Form, blank.Extract);
                    Task<RunnerProfile?> validation = app.ShowAsync<RunnerProfile>(invalid);
                    backend.FeedKey("ctrl+s"); app.PumpInputOnce(); app.RenderOnce();
                    if (validation.IsCompleted) throw new InvalidDataException("A blank participant form bypassed required-field validation.");
                    backend.FeedKey("escape"); app.PumpInputOnce(); app.PumpInputOnce();
                    RunnerProfile? cancelled = await validation.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                    if (cancelled != null) throw new InvalidDataException("Escape did not cancel the participant form.");
                    app.Stop();
                }
            }
        }
        private static Task ShellAsync(CancellationToken token)
        {
            foreach (int width in new int[] { 80, 120 })
            {
                token.ThrowIfCancellationRequested();
                HeadlessBackend backend = new HeadlessBackend(width, 24) { ClaimsTerminal = false };
                using (ArgueBotClient client = new ArgueBotClient())
                using (TuiApplication app = new TuiApplication(backend))
                {
                    new ConversationApp(client).Configure(app);
                    app.Start(); app.RenderOnce();
                    string screen = Snapshot.ToText(app.CaptureFrame() ?? throw new InvalidDataException("Terminal frame was missing."));
                    if (!screen.Contains("Conversations", StringComparison.Ordinal) || !screen.Contains("Note: Enter sends", StringComparison.Ordinal)) throw new InvalidDataException("Terminal shell omitted navigation or composer.");
                    FocusAudit.Run(app).ThrowIfProblems();
                    backend.FeedKey("ctrl+t"); app.PumpInputOnce(); app.RenderOnce();
                    if (app.Theme.Name != "Light") throw new InvalidDataException("Theme shortcut was not delivered.");
                    backend.FeedKey("ctrl+t"); app.PumpInputOnce(); app.RenderOnce();
                    if (app.Theme.Name != "Dark") throw new InvalidDataException("Theme shortcut did not toggle back.");
                    app.Stop();
                }
            }
            return Task.CompletedTask;
        }
    }
}
#endif
