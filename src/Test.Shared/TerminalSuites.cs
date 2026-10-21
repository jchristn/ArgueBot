#if NET10_0_OR_GREATER
namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Services;
    using ArgueBot.Core.Models;
    using ArgueBot.Tui;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Hosting;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Layout;
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
            get { return new List<TestSuiteDescriptor> { new TestSuiteDescriptor("Terminal", "Headless terminal", new List<TestCaseDescriptor> { new TestCaseDescriptor("Terminal", "HelpTopics", "Every help topic opens, scrolls to its final instructions and returns to the contents", HelpTopicsAsync), new TestCaseDescriptor("Terminal", "StopDelete", "Real service confirms direct stopping and combined stop/delete for active and paused debates", StopDeleteAsync), new TestCaseDescriptor("Terminal", "ContextKeys", "List actions target the highlighted row, preserve text entry and default deletion to cancellation", ContextKeysAsync), new TestCaseDescriptor("Terminal", "Navigation", "Single-click navigation ignores stale history and preserves per-conversation drafts", NavigationAsync), new TestCaseDescriptor("Terminal", "SubmitLifecycle", "Review preserves failed submissions and blocks duplicate starts or dismissal while pending", SubmitLifecycleAsync), new TestCaseDescriptor("Terminal", "MenuWrapping", "Menu labels wrap fully at narrow widths, scroll by option and retain click identity", MenuWrappingAsync), new TestCaseDescriptor("Terminal", "FirstUse", "Visible menu guides a fresh user through agent presets and review before service submission", FirstUseAsync), new TestCaseDescriptor("Terminal", "DebateSetup", "Debate wizard preserves drafts, validates steps and reviews speaker order before submission", DebateSetupAsync), new TestCaseDescriptor("Terminal", "Palette", "Quiet Charcoal uses consistent inputs, padded content and a borderless workspace", PaletteAsync), new TestCaseDescriptor("Terminal", "Transcript", "Transcript separates speakers and literal content while retaining exceptional states", TranscriptAsync), new TestCaseDescriptor("Terminal", "Splash", "Lowercase splash overlays workspace and consumes keys or closes after three seconds", SplashAsync), new TestCaseDescriptor("Terminal", "MessageReader", "Full-message reader is read-only, scrolls long Unicode text and keeps a close action visible", MessageReaderAsync), new TestCaseDescriptor("Terminal", "AttemptDetails", "Attempt inspector preserves ordered sources and scrolls through read-only provenance", AttemptDetailsAsync), new TestCaseDescriptor("Terminal", "ParticipantForms", "Participant forms preserve empty arguments, validate required fields and fit terminal sizes", ParticipantFormsAsync), new TestCaseDescriptor("Terminal", "Shell", "Responsive shell renders, traverses focus and toggles theme", ShellAsync) }) }; }
        }
        private static async Task HelpTopicsAsync(CancellationToken token)
        {
            foreach (int width in new[] { 60, 80, 120 })
            {
                HeadlessBackend backend = new HeadlessBackend(width, 24) { ClaimsTerminal = false };
                using (ArgueBotClient client = new ArgueBotClient())
                using (TuiApplication app = new TuiApplication(backend))
                {
                    ConversationApp workspace = new ConversationApp(client); workspace.Configure(app); app.Start();
                    backend.FeedKey("f1");
                    await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog, token).ConfigureAwait(false);
                    for (int topic = 0; topic < TerminalHelp.Titles.Length; topic++)
                    {
                        for (int move = 0; move < topic; move++) { backend.FeedKey("down"); app.PumpInputOnce(); }
                        backend.FeedKey("enter");
                        await PumpUntilAsync(app, () => app.Modals.Top is MessageDialog, token).ConfigureAwait(false);
                        MessageDialog reader = (MessageDialog)app.Modals.Top!;
                        if (reader.Title != TerminalHelp.Titles[topic]) throw new InvalidDataException("The Help contents opened a different topic.");
                        if (Snapshot.ToText(app.CaptureFrame()!).Contains("Press Escape to return to the Help contents.", StringComparison.Ordinal)) throw new InvalidDataException("A detailed topic opened at its end or lost its content.");
                        backend.FeedKey("end"); app.PumpInputOnce(); app.RenderOnce();
                        if (!ContentText(reader, app.CaptureFrame()!).Contains("PressEscapetoreturntotheHelpcontents.", StringComparison.Ordinal)) throw new InvalidDataException("The end of a help topic could not be reached at width " + width);
                        await SaveEvidenceAsync("help-topic-" + topic + "-" + width, Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                        backend.FeedKey("escape");
                        await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog, token).ConfigureAwait(false);
                    }
                    backend.FeedKey("escape"); await PumpUntilAsync(app, () => !workspace.IsBusy, token).ConfigureAwait(false);
                    app.Stop();
                }
            }
        }
        private static async Task StopDeleteAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-terminal-stop-" + Guid.NewGuid().ToString("N"));
            Process? service = null;
            try
            {
                TcpListener listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                await new ServiceSettings { Port = port }.SaveAsync(new ArgueBotPaths(home), token).ConfigureAwait(false);
                string endpoint = await LocalServiceConnector.ConnectAsync(home, token: token).ConfigureAwait(false);
                LocalServiceDescriptor descriptor = JsonSerializer.Deserialize<LocalServiceDescriptor>(await File.ReadAllTextAsync(Path.Combine(home, "runtime", "service.json"), token).ConfigureAwait(false)) ?? throw new InvalidDataException("No fixture service descriptor.");
                service = Process.GetProcessById(descriptor.ProcessId);
                using (ArgueBotClient client = LocalServiceConnector.CreateClient(endpoint, home))
                {
                    string fixture = Path.GetFullPath(Environment.GetEnvironmentVariable("ARGUEBOT_CLI_FIXTURE") ?? Path.Combine(AppContext.BaseDirectory, "../../../../Test.Fixtures/bin/Release/net10.0/Test.Fixtures.dll"));
                    RunnerProfile profile = await client.CreateParticipantAsync(new RunnerProfile { Name = "Terminal stop fixture", Executable = "dotnet", Arguments = new List<string> { fixture, "hang" } }, token).ConfigureAwait(false);
                    foreach (string journey in new[] { "agent_turn", "paused", "delete_agent_turn", "delete_paused" })
                    {
                        string state = journey.EndsWith("paused", StringComparison.Ordinal) ? "paused" : "agent_turn";
                        Conversation conversation = await client.CreateConversationAsync("Stop fixture " + state, token).ConfigureAwait(false);
                        DebateRun run = await client.StartAsync(conversation.Id, new CreateRunRequest { Mode = "followup", Question = "Wait for the stop-key journey", ParticipantIds = new List<string> { profile.Id }, IdempotencyKey = Guid.NewGuid().ToString("N") }, token).ConfigureAwait(false);
                        await WaitHttpRunStateAsync(client, conversation.Id, run.Id, "agent_turn", token).ConfigureAwait(false);
                        if (state == "paused")
                        {
                            await client.ActionAsync(conversation.Id, run.Id, new CommandRequest { Action = "interrupt", IdempotencyKey = Guid.NewGuid().ToString("N") }, token).ConfigureAwait(false);
                            await WaitHttpRunStateAsync(client, conversation.Id, run.Id, "paused", token).ConfigureAwait(false);
                        }
                        HeadlessBackend backend = new HeadlessBackend(80, 24) { ClaimsTerminal = false };
                        using (TuiApplication app = new TuiApplication(backend))
                        {
                            ConversationApp workspace = new ConversationApp(client); workspace.Configure(app); app.Start();
                            Task refresh = workspace.RefreshAsync(token);
                            await PumpUntilAsync(app, () => refresh.IsCompleted, token).ConfigureAwait(false); await refresh.ConfigureAwait(false);
                            app.Focus("threads"); app.RenderOnce();
                            if (!Snapshot.ToText(app.CaptureFrame()!).Split('\n').Last().Contains("s Stop", StringComparison.Ordinal)) throw new InvalidDataException("Stop shortcut is not advertised accurately.");
                            backend.FeedInput(Encoding.UTF8.GetBytes("d"));
                            await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog, token).ConfigureAwait(false);
                            ClickText(app, backend, "Delete permanently");
                            await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog && ContentText((ChoiceDialog)app.Modals.Top, app.CaptureFrame()!).Contains("Stopdebateanddeletepermanently", StringComparison.Ordinal), token).ConfigureAwait(false);
                            backend.FeedKey("enter"); await PumpUntilAsync(app, () => !workspace.IsBusy, token).ConfigureAwait(false);
                            if ((await client.RunsAsync(conversation.Id, token).ConfigureAwait(false)).Single().State != state) throw new InvalidDataException("Cancelling combined deletion stopped the debate.");
                            if (journey.StartsWith("delete_", StringComparison.Ordinal))
                            {
                                backend.FeedInput(Encoding.UTF8.GetBytes("d"));
                                await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog, token).ConfigureAwait(false);
                                ClickText(app, backend, "Delete permanently");
                                await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog && ContentText((ChoiceDialog)app.Modals.Top, app.CaptureFrame()!).Contains("Stopdebateanddeletepermanently", StringComparison.Ordinal), token).ConfigureAwait(false);
                                backend.FeedKey("down"); app.PumpInputOnce(); backend.FeedKey("enter");
                                await PumpUntilAsync(app, () => !workspace.IsBusy, token).ConfigureAwait(false);
                                if ((await client.ConversationsAsync(token: token).ConfigureAwait(false)).TotalCount != 0) throw new InvalidDataException("Combined stop/delete required a separate delete action.");
                                await SaveEvidenceAsync("stop-delete-" + journey, Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                                app.Stop();
                                continue;
                            }
                            backend.FeedInput(Encoding.UTF8.GetBytes("s"));
                            await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog, token).ConfigureAwait(false);
                            backend.FeedKey("enter"); await PumpUntilAsync(app, () => !workspace.IsBusy, token).ConfigureAwait(false);
                            if ((await client.RunsAsync(conversation.Id, token).ConfigureAwait(false)).Single().State != state) throw new InvalidDataException("Cancelling stop changed the run state.");
                            if (state == "paused") backend.FeedKey("f8"); else backend.FeedInput(Encoding.UTF8.GetBytes("s"));
                            await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog, token).ConfigureAwait(false);
                            backend.FeedKey("down"); app.PumpInputOnce(); backend.FeedKey("enter");
                            await PumpUntilAsync(app, () => !workspace.IsBusy, token).ConfigureAwait(false);
                            if ((await client.RunsAsync(conversation.Id, token).ConfigureAwait(false)).Single().State != "cancelled") throw new InvalidDataException("Direct stop did not finish the debate.");
                            if (!Snapshot.ToText(app.CaptureFrame()!).Contains("Debate stopped", StringComparison.Ordinal)) throw new InvalidDataException("Stop completion was not reported.");
                            backend.FeedInput(Encoding.UTF8.GetBytes("d"));
                            await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog, token).ConfigureAwait(false);
                            ClickText(app, backend, "Delete permanently"); await PumpUntilAsync(app, () => !workspace.IsBusy, token).ConfigureAwait(false);
                            if ((await client.ConversationsAsync(token: token).ConfigureAwait(false)).TotalCount != 0) throw new InvalidDataException("A stopped debate still blocked conversation deletion.");
                            await SaveEvidenceAsync("stop-delete-" + state, Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                            app.Stop();
                        }
                    }
                }
            }
            finally
            {
                if (service != null)
                {
                    if (!service.HasExited) service.Kill(true);
                    await service.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); service.Dispose();
                }
                if (Directory.Exists(home)) Directory.Delete(home, true);
            }
        }
        private static async Task WaitHttpRunStateAsync(ArgueBotClient client, string conversationId, string runId, string state, CancellationToken token)
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                DebateRun run = (await client.RunsAsync(conversationId, token).ConfigureAwait(false)).Single(item => item.Id == runId);
                if (run.State == state) return;
                if (run.State == "failed") throw new InvalidDataException("Stop fixture failed before reaching " + state);
                await Task.Delay(50, token).ConfigureAwait(false);
            }
            throw new InvalidDataException("Stop fixture did not reach " + state);
        }
        private static async Task ContextKeysAsync(CancellationToken token)
        {
            ConversationListView list = new ConversationListView();
            list.SetItems(new[] { "Fixture conversation" }); list.Select(0);
            string? dispatched = null;
            list.CommandRequested += command => dispatched = command;
            foreach (KeyHint hint in list.GetKeyHints() ?? Array.Empty<KeyHint>())
            {
                if (hint.Key.Length != 1) continue;
                dispatched = null;
                if (!list.HandleKey(KeyEvent.Char(hint.Key[0])) || dispatched != hint.Key) throw new InvalidDataException("Displayed shortcut does not invoke its command: " + hint.Key);
            }
            foreach (char shortcut in "dsbrlfne")
                if (!(list.GetKeyHints() ?? Array.Empty<KeyHint>()).Any(hint => hint.Key == shortcut.ToString())) throw new InvalidDataException("A case-sensitive shortcut lost its lowercase label: " + shortcut);
            foreach (int width in new[] { 60, 80, 120 })
            {
                HeadlessBackend backend = new HeadlessBackend(width, 24) { ClaimsTerminal = false };
                TerminalNavigationHandler service = new TerminalNavigationHandler();
                using (ArgueBotClient client = new ArgueBotClient("http://127.0.0.1:4317", service))
                using (TuiApplication app = new TuiApplication(backend))
                {
                    ConversationApp workspace = new ConversationApp(client); workspace.Configure(app); app.Start();
                    if (app.CurrentFocusPath.Region != "threads") throw new InvalidDataException("The workspace did not begin in conversations.");
                    Task refresh = workspace.RefreshAsync(token);
                    await PumpUntilAsync(app, () => refresh.IsCompleted, token).ConfigureAwait(false); await refresh.ConfigureAwait(false);
                    app.Focus("threads"); app.RenderOnce();
                    string footer = Snapshot.ToText(app.CaptureFrame()!).Split('\n').Last();
                    if (!footer.Contains("d Delete", StringComparison.Ordinal) || footer.Contains("D Delete", StringComparison.Ordinal)) throw new InvalidDataException("The focused list hides its delete shortcut at width " + width);
                    backend.FeedKey("down"); app.PumpInputOnce();
                    backend.FeedInput(Encoding.UTF8.GetBytes("d"));
                    await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog, token).ConfigureAwait(false);
                    string confirmation = ContentText((ChoiceDialog)app.Modals.Top!, app.CaptureFrame()!);
                    if (!confirmation.Contains("Beta", StringComparison.Ordinal) || confirmation.Contains("Alpha", StringComparison.Ordinal)) throw new InvalidDataException("Delete targeted the open conversation instead of the highlighted row.");
                    backend.FeedKey("enter");
                    await PumpUntilAsync(app, () => !workspace.IsBusy, token).ConfigureAwait(false);
                    if (service.DeletedId != null) throw new InvalidDataException("Delete confirmation did not default to Cancel.");
                    backend.FeedKey("alt+2"); app.PumpInputOnce(); app.RenderOnce();
                    if (app.CurrentFocusPath.Region != "transcript" || !Snapshot.ToText(app.CaptureFrame()!).Contains("› Beta", StringComparison.Ordinal)) throw new InvalidDataException("History navigation lost its focus indicator.");
                    backend.FeedKey("alt+3"); app.PumpInputOnce();
                    if (app.CurrentFocusPath.Region != "composer") throw new InvalidDataException("Direct note navigation failed.");
                    backend.FeedInput(Encoding.UTF8.GetBytes("dnbprflgea/?")); app.PumpInputOnce(); app.RenderOnce();
                    if (app.Modals.Top != null || !Snapshot.ToText(app.CaptureFrame()!).Contains("dnbprflgea/?", StringComparison.Ordinal)) throw new InvalidDataException("List shortcuts intercepted note typing.");
                    backend.FeedKey("escape");
                    await PumpUntilAsync(app, () => app.CurrentFocusPath.Region == "threads", token).ConfigureAwait(false);
                    if (app.CurrentFocusPath.Region != "threads") throw new InvalidDataException("Escape did not return to conversations.");
                    backend.FeedKey("delete");
                    await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog, token).ConfigureAwait(false);
                    backend.FeedKey("down"); app.PumpInputOnce(); backend.FeedKey("enter");
                    await PumpUntilAsync(app, () => !workspace.IsBusy, token).ConfigureAwait(false);
                    if (service.DeletedId != "beta") throw new InvalidDataException("Confirmed Delete key did not delete the highlighted conversation.");
                    await SaveEvidenceAsync("context-keys-" + width, Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                    app.Stop();
                }
            }
        }
        private static async Task NavigationAsync(CancellationToken token)
        {
            HeadlessBackend backend = new HeadlessBackend(80, 24) { ClaimsTerminal = false };
            TerminalNavigationHandler service = new TerminalNavigationHandler();
            using (ArgueBotClient client = new ArgueBotClient("http://127.0.0.1:4317", service))
            using (TuiApplication app = new TuiApplication(backend))
            {
                ConversationApp workspace = new ConversationApp(client); workspace.Configure(app); app.Start();
                Task refresh = workspace.RefreshAsync(token);
                await PumpUntilAsync(app, () => refresh.IsCompleted, token).ConfigureAwait(false); await refresh.ConfigureAwait(false);
                service.DelayNextRead = true;
                Task oldRead = workspace.RefreshAsync(token);
                await PumpUntilAsync(app, () => service.ReadStarted.Task.IsCompleted, token).ConfigureAwait(false);
                ClickText(app, backend, "Beta");
                await PumpUntilAsync(app, () => Snapshot.ToText(app.CaptureFrame()!).Contains("Opening Beta", StringComparison.Ordinal), token).ConfigureAwait(false);
                service.ReleaseRead.TrySetResult();
                await PumpUntilAsync(app, () => oldRead.IsCompleted, token).ConfigureAwait(false); await oldRead.ConfigureAwait(false);
                if (Snapshot.ToText(app.CaptureFrame()!).Contains("History for Alpha", StringComparison.Ordinal)) throw new InvalidDataException("An old response replaced the newly opened conversation.");
                refresh = workspace.RefreshAsync(token);
                await PumpUntilAsync(app, () => refresh.IsCompleted, token).ConfigureAwait(false); await refresh.ConfigureAwait(false);
                if (!Snapshot.ToText(app.CaptureFrame()!).Contains("History for Beta", StringComparison.Ordinal)) throw new InvalidDataException("Single-click navigation did not open the chosen history.");
                app.Focus("composer"); backend.FeedInput(Encoding.UTF8.GetBytes("Draft for Beta")); app.PumpInputOnce(); app.RenderOnce();
                ClickText(app, backend, "Alpha");
                await PumpUntilAsync(app, () => Snapshot.ToText(app.CaptureFrame()!).Contains("Opening Alpha", StringComparison.Ordinal), token).ConfigureAwait(false);
                app.Focus("composer"); backend.FeedInput(Encoding.UTF8.GetBytes("Draft for Alpha")); app.PumpInputOnce(); app.RenderOnce();
                ClickText(app, backend, "Beta");
                await PumpUntilAsync(app, () => Snapshot.ToText(app.CaptureFrame()!).Contains("Opening Beta", StringComparison.Ordinal), token).ConfigureAwait(false);
                if (!Snapshot.ToText(app.CaptureFrame()!).Contains("Draft for Beta", StringComparison.Ordinal)) throw new InvalidDataException("Switching conversations lost or mixed their note drafts.");
                app.Stop();
            }
        }
        private static async Task SubmitLifecycleAsync(CancellationToken token)
        {
            HeadlessBackend backend = new HeadlessBackend(80, 24) { ClaimsTerminal = false };
            using (TuiApplication app = new TuiApplication(backend))
            {
                app.Theme = TerminalTheme.Dark; app.ApplyThemeToWidgets = true; app.Start();
                List<RunnerProfile> profiles = new List<RunnerProfile> { AgentPresets.Create(0), AgentPresets.Create(1) };
                profiles[0].Id = "fixture-one"; profiles[1].Id = "fixture-two";
                DebateSetupEditor editor = new DebateSetupEditor(profiles, "Keep this question after a failed start", false);
                TaskCompletionSource pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                List<string> commands = new List<string>();
                DebateWizardDialog dialog = new DebateWizardDialog(editor, app, request => { commands.Add(request.IdempotencyKey); return commands.Count == 1 ? pending.Task : Task.CompletedTask; });
                Task<CreateRunRequest?> result = app.ShowAsync<CreateRunRequest>(dialog);
                dialog.Wizard.Next(); dialog.Wizard.Next(); dialog.Wizard.Next(); dialog.Wizard.Next(); app.RenderOnce();
                if (dialog.CanClose || dialog.RequestClose(null)) throw new InvalidDataException("A pending start could discard its state.");
                dialog.Wizard.Next();
                if (commands.Count != 1) throw new InvalidDataException("A pending start was submitted twice.");
                pending.SetException(new InvalidOperationException("The service is temporarily unavailable."));
                await PumpUntilAsync(app, () => dialog.CanClose && ContentText(dialog, app.CaptureFrame()!).Contains("temporarilyunavailable", StringComparison.Ordinal), token).ConfigureAwait(false);
                if (result.IsCompleted || dialog.Wizard.CurrentIndex != 3 || ((TextField)editor.QuestionForm.GetField(0)).Value != "Keep this question after a failed start") throw new InvalidDataException("A failed start discarded its review or draft.");
                await SaveEvidenceAsync("debate-start-error", Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                ClickText(app, backend, "[ Start debate ]");
                await PumpUntilAsync(app, () => result.IsCompleted, token).ConfigureAwait(false);
                if (await result.ConfigureAwait(false) == null || commands.Count != 2 || commands[0] != commands[1]) throw new InvalidDataException("Explicit retry lost its command identity.");
                app.Stop();
            }
        }
        private static async Task MenuWrappingAsync(CancellationToken token)
        {
            string[] topics = TerminalHelp.Titles;
            foreach (int width in new[] { 40, 60, 80, 120 })
            {
                HeadlessBackend backend = new HeadlessBackend(width, 24) { ClaimsTerminal = false };
                using (TuiApplication app = new TuiApplication(backend))
                {
                    app.ApplyThemeToWidgets = true; app.Theme = TerminalTheme.Dark; app.Start();
                    ChoiceDialog dialog = new ChoiceDialog("Help", topics);
                    Task<object?> result = app.ShowAsync(dialog); app.RenderOnce();
                    CellBuffer frame = app.CaptureFrame()!;
                    StringBuilder text = new StringBuilder(); Rect bounds = dialog.ContentBounds;
                    for (int row = bounds.Y; row < bounds.Y + bounds.Height; row++)
                        for (int column = bounds.X; column < bounds.X + bounds.Width; column++) text.Append(frame.Get(column, row).Grapheme);
                    string visible = string.Concat(text.ToString().Where(character => !char.IsWhiteSpace(character)));
                    foreach (string topic in topics)
                        if (!visible.Contains(string.Concat(topic.Where(character => !char.IsWhiteSpace(character))), StringComparison.Ordinal)) throw new InvalidDataException("A help option was clipped at width " + width + ": " + topic);
                    await SaveEvidenceAsync("help-wrapped-" + width, Snapshot.ToText(frame), token).ConfigureAwait(false);
                    ClickText(app, backend, "Troubleshooting");
                    await PumpUntilAsync(app, () => result.IsCompleted, token).ConfigureAwait(false);
                    if (await result.ConfigureAwait(false) is not int selected || topics[selected] != "Troubleshooting failed or paused debates") throw new InvalidDataException("Wrapped help text selected the wrong topic.");
                    string[] longList = Enumerable.Range(0, 20).Select(index => "Option " + index + ": this full description needs to remain readable").ToArray();
                    ChoiceDialog scrolling = new ChoiceDialog("Actions", longList);
                    Task<object?> scrollResult = app.ShowAsync(scrolling); app.RenderOnce();
                    backend.FeedKey("end"); app.PumpInputOnce(); app.RenderOnce();
                    if (!Snapshot.ToText(app.CaptureFrame()!).Contains("Option 19:", StringComparison.Ordinal)) throw new InvalidDataException("Wrapped menu did not reveal its last option.");
                    Rect scrollbar = scrolling.ContentBounds;
                    backend.FeedMouse(new MouseEvent(MouseEventKind.Press, MouseButton.Left, scrollbar.X + scrollbar.Width - 1, scrollbar.Y, KeyModifiers.None, 1)); app.PumpInputOnce(); app.RenderOnce();
                    if (scrollResult.IsCompleted) throw new InvalidDataException("Clicking a menu scrollbar activated an option.");
                    backend.FeedKey("end"); app.PumpInputOnce();
                    backend.FeedKey("enter"); app.PumpInputOnce();
                    if (await scrollResult.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false) is not int last || last != 19) throw new InvalidDataException("Wrapped menu lost keyboard selection identity.");
                    app.Stop();
                }
            }
        }
        private static async Task FirstUseAsync(CancellationToken token)
        {
            RunnerProfile mux = new ParticipantEditor(AgentPresets.Create(3)).Extract();
            if (mux.Provider != "mux" || mux.Kind != "cli" || mux.Executable != "mux" || !mux.Arguments.Contains("--no-session-persistence")) throw new InvalidDataException("Mux preset did not retain its executable, protocol and default arguments through the agent form.");
            HeadlessBackend backend = new HeadlessBackend(80, 24) { ClaimsTerminal = false };
            TerminalJourneyHandler service = new TerminalJourneyHandler();
            using (ArgueBotClient client = new ArgueBotClient("http://127.0.0.1:4317", service))
            using (TuiApplication app = new TuiApplication(backend))
            {
                ConversationApp workspace = new ConversationApp(client); workspace.Configure(app); app.Start(); app.RenderOnce();
                string[] rows = Snapshot.ToText(app.CaptureFrame()!).Split('\n');
                int menuColumn = rows[1].IndexOf("Menu", StringComparison.Ordinal);
                if (menuColumn < 0) throw new InvalidDataException("The main menu was not visible.");
                ClickText(app, backend, "Menu");
                await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog menu && menu.Title?.StartsWith("Main menu", StringComparison.Ordinal) == true, token).ConfigureAwait(false);
                ClickText(app, backend, "New debate or conversation");
                await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog menu && menu.Title?.StartsWith("New:", StringComparison.Ordinal) == true, token).ConfigureAwait(false);
                ClickText(app, backend, "Start a debate: guided setup");
                for (int agent = 0; agent < 2; agent++)
                {
                    await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog menu && menu.Title?.StartsWith("Set up agents", StringComparison.Ordinal) == true, token).ConfigureAwait(false);
                    ClickText(app, backend, "Add or enable an agent");
                    await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog menu && menu.Title?.StartsWith("Agents:", StringComparison.Ordinal) == true, token).ConfigureAwait(false);
                    ClickText(app, backend, "Add agent: CLI harness or REST model");
                    await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog menu && menu.Title == "Add agent: choose how it runs", token).ConfigureAwait(false);
                    ClickText(app, backend, agent == 0 ? "Claude Code (installed CLI)" : "Codex (installed CLI)");
                    await PumpUntilAsync(app, () => app.Modals.Top is FormDialog<RunnerProfile>, token).ConfigureAwait(false);
                    for (int field = 0; field < 40; field++)
                    {
                        app.RenderOnce();
                        if (Snapshot.ToText(app.CaptureFrame()!).Contains("[ Save ]", StringComparison.Ordinal)) break;
                        backend.FeedKey("tab"); app.PumpInputOnce();
                    }
                    ClickText(app, backend, "[ Save ]");
                    int count = agent + 1;
                    await PumpUntilAsync(app, () => service.Profiles.Count == count, token).ConfigureAwait(false);
                }
                await PumpUntilAsync(app, () => app.Modals.Top is DebateWizardDialog, token).ConfigureAwait(false);
                DebateWizardDialog dialog = (DebateWizardDialog)app.Modals.Top!;
                Form question = dialog.Editor.QuestionForm;
                ((TextField)question.GetField(0)).Value = "Which architecture should we use?";
                backend.FeedKey("enter");
                await PumpUntilAsync(app, () => dialog.Wizard.CurrentIndex == 1, token).ConfigureAwait(false);
                backend.FeedKey("alt+right");
                await PumpUntilAsync(app, () => dialog.Wizard.CurrentIndex == 2, token).ConfigureAwait(false);
                backend.FeedKey("alt+right");
                await PumpUntilAsync(app, () => dialog.Wizard.CurrentIndex == 3, token).ConfigureAwait(false);
                if (service.Conversation != null || service.Started != null) throw new InvalidDataException("Setup wrote a conversation or started agents before review confirmation.");
                await SaveEvidenceAsync("first-use-review", Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                ClickText(app, backend, "[ Start debate ]");
                await PumpUntilAsync(app, () => service.Started != null, token).ConfigureAwait(false);
                if (service.Conversation?.Title != "Which architecture should we use?" || service.Started?.ParticipantIds.Count != 2 || service.Profiles[0].Provider != "claude" || service.Profiles[1].Provider != "codex") throw new InvalidDataException("First-use setup lost its question or selected agent presets.");
                await PumpUntilAsync(app, () => !workspace.IsBusy, token).ConfigureAwait(false);
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    ClickText(app, backend, "Menu");
                    await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog menu && menu.Title?.StartsWith("Main menu", StringComparison.Ordinal) == true, token).ConfigureAwait(false);
                    ClickText(app, backend, "Delete this conversation");
                    await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog menu && menu.Title == "Choose an action", token).ConfigureAwait(false);
                    string confirmation = ContentText((DialogModal)app.Modals.Top!, app.CaptureFrame()!);
                    if (!confirmation.Contains("cannotbeundone", StringComparison.Ordinal)) throw new InvalidDataException("Delete confirmation did not explain permanent removal.");
                    service.RejectDelete = attempt == 1;
                    ClickText(app, backend, attempt == 0 ? "Cancel: keep this conversation" : "Delete permanently");
                    if (attempt == 1)
                    {
                        await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog error && error.Title == "Choose an action", token).ConfigureAwait(false);
                        if (service.Deleted) throw new InvalidDataException("An active-run refusal was ignored.");
                        backend.FeedKey("escape");
                    }
                    await PumpUntilAsync(app, () => !workspace.IsBusy, token).ConfigureAwait(false);
                    if (attempt < 2 && service.Deleted) throw new InvalidDataException("Conversation was deleted after cancel or an active-run refusal.");
                }
                if (!service.Deleted) throw new InvalidDataException("Confirmed stopped conversation deletion was not submitted.");
                app.Stop();
            }
        }
        private static string ContentText(DialogModal dialog, CellBuffer frame)
        {
            StringBuilder text = new StringBuilder(); Rect bounds = dialog.ContentBounds;
            for (int row = bounds.Y; row < bounds.Y + bounds.Height; row++)
                for (int column = bounds.X; column < bounds.X + bounds.Width; column++) text.Append(frame.Get(column, row).Grapheme);
            return string.Concat(text.ToString().Where(character => !char.IsWhiteSpace(character)));
        }
        private static void ClickText(TuiApplication app, HeadlessBackend backend, string text)
        {
            app.RenderOnce(); string[] rows = Snapshot.ToText(app.CaptureFrame()!).Split('\n');
            int row = Array.FindIndex(rows, line => line.Contains(text, StringComparison.Ordinal));
            if (row < 0) throw new InvalidDataException("Expected a visible clickable action: " + text);
            int column = rows[row].IndexOf(text, StringComparison.Ordinal) + 1;
            backend.FeedMouse(new MouseEvent(MouseEventKind.Press, MouseButton.Left, column, row, KeyModifiers.None, 1));
            backend.FeedMouse(new MouseEvent(MouseEventKind.Release, MouseButton.Left, column, row, KeyModifiers.None, 1));
        }
        private static async Task PumpUntilAsync(TuiApplication app, Func<bool> condition, CancellationToken token)
        {
            for (int index = 0; index < 200; index++)
            {
                token.ThrowIfCancellationRequested(); app.PumpInputOnce(); app.RenderOnce();
                if (condition()) return;
                await Task.Delay(10, token).ConfigureAwait(false);
            }
            throw new InvalidDataException("Terminal journey did not reach the expected screen. Current modal: " + (app.Modals.Top is DialogModal dialog ? dialog.Title : "none"));
        }
        private static async Task DebateSetupAsync(CancellationToken token)
        {
            foreach (Theme theme in new[] { TerminalTheme.Dark, Theme.Light })
            {
                HeadlessBackend backend = new HeadlessBackend(80, 24) { ClaimsTerminal = false };
                using (TuiApplication app = new TuiApplication(backend))
                {
                    app.Theme = theme; app.ApplyThemeToWidgets = true; app.Start();
                    List<RunnerProfile> profiles = new List<RunnerProfile> { new RunnerProfile { Id = "one", Name = "Claude" }, new RunnerProfile { Id = "two", Name = "Codex" }, new RunnerProfile { Id = "three", Name = "Gemini" } };
                    DebateSetupEditor editor = new DebateSetupEditor(profiles, "", false);
                    DebateWizardDialog dialog = new DebateWizardDialog(editor);
                    Task<CreateRunRequest?> result = app.ShowAsync<CreateRunRequest>(dialog);
                    if (dialog.Wizard.Next()) throw new InvalidDataException("The wizard accepted an empty question.");
                    ((TextField)editor.QuestionForm.GetField(0)).Value = "Compare these approaches";
                    app.RenderOnce();
                    await SaveEvidenceAsync("debate-question-" + theme.Name, Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                    backend.FeedKey("f1"); app.PumpInputOnce(); app.RenderOnce();
                    if (!Snapshot.ToText(app.CaptureFrame()!).Contains("Back to dialog", StringComparison.Ordinal)) throw new InvalidDataException("F1 did not provide help inside the wizard.");
                    backend.FeedKey("escape");
                    await PumpUntilAsync(app, () => !Snapshot.ToText(app.CaptureFrame()!).Contains("Back to dialog", StringComparison.Ordinal), token).ConfigureAwait(false);
                    if (dialog.IsClosed || ((TextField)editor.QuestionForm.GetField(0)).Value != "Compare these approaches") throw new InvalidDataException("Context help lost the debate draft.");
                    backend.FeedKey("enter"); app.PumpInputOnce();
                    if (dialog.Wizard.CurrentIndex != 1) throw new InvalidDataException("Enter did not advance from the question.");
                    ((Dropdown<string>)editor.SpeakersForm.GetField(1)).SelectedIndex = 0;
                    if (dialog.Wizard.Next()) throw new InvalidDataException("Duplicate speakers were accepted.");
                    ((Dropdown<string>)editor.SpeakersForm.GetField(1)).SelectedIndex = 1;
                    ((Dropdown<string>)editor.SpeakersForm.GetField(2)).SelectedIndex = 3;
                    dialog.Wizard.Next(); dialog.Wizard.Back(); app.RenderOnce();
                    editor.SpeakersForm.SetFocusedField(2); backend.FeedKey("enter"); app.PumpInputOnce(); app.RenderOnce();
                    if (!((Dropdown<string>)editor.SpeakersForm.GetField(2)).IsOpen) throw new InvalidDataException("The final-agent dropdown did not open.");
                    await SaveEvidenceAsync("debate-expanded-speakers-" + theme.Name, Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                    backend.FeedKey("escape");
                    await PumpUntilAsync(app, () => !((Dropdown<string>)editor.SpeakersForm.GetField(2)).IsOpen, token).ConfigureAwait(false);
                    if (dialog.IsClosed || ((Dropdown<string>)editor.SpeakersForm.GetField(2)).IsOpen) throw new InvalidDataException("Escape discarded the wizard instead of closing the dropdown.");
                    string speakers = Snapshot.ToText(app.CaptureFrame()!);
                    if (!speakers.Contains("Next", StringComparison.Ordinal) || !speakers.Contains("Cancel", StringComparison.Ordinal) || !speakers.Contains("Third agent", StringComparison.Ordinal)) throw new InvalidDataException("Speaker choices or navigation were hidden.");
                    await SaveEvidenceAsync("debate-speakers-" + theme.Name, speakers, token).ConfigureAwait(false);
                    dialog.Wizard.Next();
                    ((TextField)editor.SettingsForm.GetField(0)).Value = "51";
                    if (dialog.Wizard.Next()) throw new InvalidDataException("An invalid round limit was accepted.");
                    ((TextField)editor.SettingsForm.GetField(0)).Value = "6";
                    app.RenderOnce();
                    await SaveEvidenceAsync("debate-settings-" + theme.Name, Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                    dialog.Wizard.Back();
                    if (((Dropdown<string>)editor.SpeakersForm.GetField(2)).SelectedIndex != 3) throw new InvalidDataException("Back lost the speaker selection.");
                    dialog.Wizard.Next(); dialog.Wizard.Next(); app.RenderOnce();
                    string review = Snapshot.ToText(app.CaptureFrame()!);
                    if (!review.Contains("Start debate", StringComparison.Ordinal) || !review.Contains("Closes each round: Gemini", StringComparison.Ordinal)) throw new InvalidDataException("Review omitted the ending speaker or Start action.");
                    await SaveEvidenceAsync("debate-review-" + theme.Name, review, token).ConfigureAwait(false);
                    if (result.IsCompleted) throw new InvalidDataException("The debate started before the review was confirmed.");
                    dialog.Wizard.Next();
                    CreateRunRequest? request = await result.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                    if (request == null || request.Question != "Compare these approaches" || !request.ParticipantIds.SequenceEqual(new[] { "one", "two", "three" }) || request.MaxRounds != 6 || request.InterventionSeconds != 15) throw new InvalidDataException("Debate setup changed the prompt, speaker order or settings.");
                    app.Stop();
                }
            }
        }
        private static async Task PaletteAsync(CancellationToken token)
        {
            HeadlessBackend backend = new HeadlessBackend(80, 24) { ClaimsTerminal = false };
            using (ArgueBotClient client = new ArgueBotClient())
            using (TuiApplication app = new TuiApplication(backend))
            {
                new ConversationApp(client).Configure(app); app.Start(); app.RenderOnce();
                CellBuffer frame = app.CaptureFrame()!;
                Color charcoal = Color.FromRgb(0x17, 0x19, 0x1C); Color input = Color.FromRgb(0x25, 0x28, 0x2D);
                if (!frame.Get(30, 5).Style.Background.Equals(charcoal) || !frame.Get(2, 20).Style.Background.Equals(input)) throw new InvalidDataException("Workspace and input backgrounds do not use the shared palette.");
                foreach (int column in new[] { 0, 79 })
                {
                    if (frame.Get(column, 20).Grapheme != " ") throw new InvalidDataException("Composer or sidebar content touches its frame.");
                }
                if (Snapshot.ToText(frame).IndexOfAny(new[] { '┌', '┐', '└', '┘', '┏', '┓', '┗', '┛' }) >= 0) throw new InvalidDataException("The workspace reintroduced rectangular pane frames.");
                if (frame.Get(23, 10).Grapheme != "│") throw new InvalidDataException("The sidebar divider disappeared.");
                CellBuffer listBuffer = new CellBuffer(30, 3);
                ConversationListView list = new ConversationListView(); list.ApplyTheme(TerminalTheme.Dark);
                list.SetItems(new[] { "議論 with Mux" }); list.Select(0); list.OnFocusChanged(true); list.Render(new BufferSurface(listBuffer));
                if (!listBuffer.Get(0, 0).Style.Equals(TerminalTheme.Dark.Selection) || !Snapshot.ToText(listBuffer).Contains("議論 with Mux", StringComparison.Ordinal)) throw new InvalidDataException("Conversation selection lost its quiet palette or wide Unicode text.");
                await SaveEvidenceAsync("palette-workspace", Snapshot.ToText(frame), token).ConfigureAwait(false);
                string footer = Snapshot.ToText(frame).Split('\n').Last().Trim();
                if (!footer.StartsWith("F1 Help", StringComparison.Ordinal)) throw new InvalidDataException("Help must lead the terminal footer.");
                backend.FeedKey("f1"); app.PumpInputOnce();
                for (int index = 0; index < 30 && !app.Modals.IsActive; index++) { app.PumpInputOnce(); await Task.Delay(10, token).ConfigureAwait(false); }
                app.RenderOnce();
                string helpScreen = Snapshot.ToText(app.CaptureFrame()!);
                if (!helpScreen.Contains("Configure agents and harnesses", StringComparison.Ordinal)) throw new InvalidDataException("F1 did not open the help topic menu.");
                await SaveEvidenceAsync("help-menu", helpScreen, token).ConfigureAwait(false);
                backend.FeedKey("escape");
                for (int index = 0; index < 30 && app.Modals.IsActive; index++) { app.PumpInputOnce(); await Task.Delay(10, token).ConfigureAwait(false); }
                if (app.Modals.IsActive) throw new InvalidDataException("Help menu did not close on Escape.");
                Task<string?> prompt = app.PromptStyledAsync("New conversation", "A question"); app.RenderOnce();
                if (app.Modals.Top is not TextPromptDialog) throw new InvalidDataException("A prompt bypassed the shared dialog style.");
                await SaveEvidenceAsync("palette-prompt", Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                backend.FeedKey("enter");
                for (int index = 0; index < 30 && !prompt.IsCompleted; index++) { app.PumpInputOnce(); await Task.Delay(10, token).ConfigureAwait(false); }
                if (await prompt.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false) != "A question") throw new InvalidDataException("Styled prompt changed its initial value.");
                Task<int> choice = app.SelectStyledAsync("Participants", "First", "Second");
                backend.FeedKey("down"); backend.FeedKey("enter"); app.PumpInputOnce(); app.PumpInputOnce();
                if (await choice.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false) != 1) throw new InvalidDataException("Styled choice lost keyboard selection.");
                ParticipantEditor editor = new ParticipantEditor();
                FormDialog<RunnerProfile> dialog = new FormDialog<RunnerProfile>("Participant", editor.Form, editor.Extract);
                Task<object?> form = app.ShowAsync(dialog); app.RenderOnce();
                if (!((TextField)editor.Form.GetField(0)).NormalStyle.Background.Equals(input)) throw new InvalidDataException("Form fields bypassed the shared input background.");
                await SaveEvidenceAsync("palette-participant", Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                backend.FeedKey("escape");
                for (int index = 0; index < 30 && !form.IsCompleted; index++) { app.PumpInputOnce(); await Task.Delay(10, token).ConfigureAwait(false); }
                await form.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                app.Stop();
            }
        }
        private static async Task TranscriptAsync(CancellationToken token)
        {
            if (!TranscriptFormatter.Heading(new Message { Speaker = "REST fixture", Kind = "tool", Content = "Local tool: read_file\nStatus: completed" }).Contains("Local tool", StringComparison.Ordinal)) throw new InvalidDataException("Tool history lacked its local-tool label.");

            foreach (Theme theme in new[] { Theme.Dark, Theme.Light })
            {
                HeadlessBackend backend = new HeadlessBackend(80, 30) { ClaimsTerminal = false };
                using (TuiApplication app = new TuiApplication(backend))
                {
                    app.Theme = theme;
                    Pane pane = new Pane("transcript");
                    app.AddWidget("transcript", new TranscriptView(pane), region => region.FillWidth().FillHeight());
                    List<Message> messages = new List<Message>
                    {
                        new Message { Speaker = "user", Content = "Compare the two approaches. [red] stays literal." },
                        new Message { Speaker = "Claude", Kind = "answer", Content = "First, consider the trade-offs.\n\nKeep the explanation readable." },
                        new Message { Speaker = "Codex", Kind = "answer", State = "interrupted", Content = "Partial answer", ContentTruncated = true }
                    };
                    TranscriptFormatter.Render(pane, messages, theme);
                    app.Start(); app.RenderOnce();
                    string screen = Snapshot.ToText(app.CaptureFrame()!);
                    if (!screen.Contains("You", StringComparison.Ordinal) || !screen.Contains("Claude", StringComparison.Ordinal) || !screen.Contains("Interrupted", StringComparison.Ordinal) || !screen.Contains("[red] stays literal", StringComparison.Ordinal) || !screen.Contains("Ctrl+L", StringComparison.Ordinal) || screen.Contains("completed", StringComparison.Ordinal) || screen.Contains("user | note", StringComparison.Ordinal) || screen.Contains("trade-offs.Keep", StringComparison.Ordinal)) throw new InvalidDataException("Transcript lost readable headings, original content or exceptional-state notices.");
                    string[] transcriptRows = screen.Split('\n');
                    int userRow = Array.FindIndex(transcriptRows, row => row.Trim() == "You");
                    int agentRow = Array.FindIndex(transcriptRows, row => row.Trim() == "Claude");
                    if (userRow < 0 || agentRow < 0 || !transcriptRows[userRow + 1].Contains("Compare the two approaches", StringComparison.Ordinal) || !transcriptRows[agentRow + 1].Contains("First, consider", StringComparison.Ordinal)) throw new InvalidDataException("Message headings must be immediately followed by their content.");
                    if (!TranscriptFormatter.Heading(new Message { Kind = "steer" }).Contains("Private directive", StringComparison.Ordinal)) throw new InvalidDataException("Private directive label disappeared.");
                    await SaveEvidenceAsync("transcript-" + theme.Name, screen, token).ConfigureAwait(false);
                    pane.Clear();
                    for (int line = 0; line < 80; line++) pane.WriteLine("History line " + line);
                    app.Focus("transcript"); app.RenderOnce();
                    backend.FeedKey("home"); app.PumpInputOnce(); app.RenderOnce();
                    if (pane.IsAtBottom || !Snapshot.ToText(app.CaptureFrame()!).Contains("History line 0", StringComparison.Ordinal)) throw new InvalidDataException("Keyboard history navigation did not reach older content.");
                    backend.FeedKey("end"); app.PumpInputOnce(); app.RenderOnce();
                    if (!pane.IsAtBottom || !Snapshot.ToText(app.CaptureFrame()!).Contains("History line 79", StringComparison.Ordinal)) throw new InvalidDataException("End did not return history to the latest content.");
                    app.Stop();
                }
            }
        }
        private static async Task SplashAsync(CancellationToken token)
        {
            foreach (bool pressKey in new[] { true, false })
            {
                HeadlessBackend backend = new HeadlessBackend(80, 24) { ClaimsTerminal = false };
                using (ArgueBotClient client = new ArgueBotClient())
                using (TuiApplication app = new TuiApplication(backend))
                {
                    new ConversationApp(client).Configure(app); app.Start();
                    Task splash = SplashScreen.ShowAsync(app, token);
                    app.RenderOnce();
                    string screen = Snapshot.ToText(app.CaptureFrame()!);
                    if (!screen.Contains("F1 Help", StringComparison.Ordinal) || !screen.Contains("|___/", StringComparison.Ordinal) || !screen.Contains("Press any key", StringComparison.Ordinal) || !screen.Contains("v0.1.0", StringComparison.Ordinal) || !screen.Contains("2026 Joel Christner", StringComparison.Ordinal) || !screen.Contains("https://github.com/jchristn/ArgueBot", StringComparison.Ordinal) || !screen.Contains("MIT License", StringComparison.Ordinal) || !screen.Contains("Ctrl+E: configure agents", StringComparison.Ordinal)) throw new InvalidDataException("Splash did not preserve its lowercase logo and surrounding workspace.");
                    string[] rows = screen.Split('\n');
                    int hintRow = Array.FindIndex(rows, row => row.Contains("Press any key", StringComparison.Ordinal));
                    if (hintRow < 0 || !rows[hintRow + 1].Contains(new string(' ', 62), StringComparison.Ordinal)) throw new InvalidDataException("Splash needs an empty interior row below its final text.");
                    foreach (string group in new[] { "Let your agents", "https://github.com", "Ctrl+E: configure" })
                    {
                        int row = Array.FindIndex(rows, line => line.Contains(group, StringComparison.Ordinal));
                        if (row <= 0 || !rows[row - 1].Contains(new string(' ', 62), StringComparison.Ordinal)) throw new InvalidDataException("Splash text groups lost their spacing.");
                    }
                    await SaveEvidenceAsync("splash", screen, token).ConfigureAwait(false);
                    if (pressKey) { backend.FeedKey("ctrl+n"); app.PumpInputOnce(); }
                    else
                    {
                        await Task.Delay(2800, token).ConfigureAwait(false);
                        if (splash.IsCompleted) throw new InvalidDataException("Splash closed before three seconds.");
                        await Task.Delay(400, token).ConfigureAwait(false);
                        app.PumpInputOnce();
                    }
                    await splash.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                    app.RenderOnce();
                    if (app.Modals.IsActive) throw new InvalidDataException("Splash dismissal activated a workspace shortcut or left its modal open.");
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
                    dialog.HandlePaste("attempted mutation"); app.RenderOnce();
                    string first = Snapshot.ToText(app.CaptureFrame()!);
                    if (!first.Contains("議論🙂 1", StringComparison.Ordinal) || first.Contains("COMPLETE MESSAGE TAIL", StringComparison.Ordinal)) throw new InvalidDataException("A full message did not open at its beginning.");
                    backend.FeedKey("pagedown"); app.PumpInputOnce(); app.RenderOnce();
                    if (Snapshot.ToText(app.CaptureFrame()!) == first) throw new InvalidDataException("PageDown did not scroll the read-only message.");
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
        private static async Task SaveEvidenceAsync(string name, string content, CancellationToken token)
        {
            string? folder = Environment.GetEnvironmentVariable("ARGUEBOT_TUI_EVIDENCE_DIR");
            if (string.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, name + ".txt"), content, token).ConfigureAwait(false);
        }
        private static async Task ParticipantFormsAsync(CancellationToken token)
        {
            RunnerProfile restProfile = new RunnerProfile { Name = "REST tools", Kind = "rest", Provider = "ollama", Endpoint = "http://127.0.0.1:11434", Model = "fixture", WorkingDirectory = Path.GetTempPath() };
            if (new ParticipantEditor(restProfile).Extract().WorkingDirectory != restProfile.WorkingDirectory) throw new InvalidDataException("REST form discarded its local tool working directory.");
            ParticipantEditor defaults = new ParticipantEditor();
            if (!defaults.Extract().Arguments.Contains("--dangerously-skip-permissions")) throw new InvalidDataException("New Claude Code form omitted unattended arguments.");
            ((Dropdown<string>)defaults.Form.GetField(2)).SelectedIndex = 2;
            RunnerProfile codex = defaults.Extract();
            if (codex.Provider != "codex" || codex.Executable != "codex" || !codex.Arguments.Contains("--yolo") || codex.Arguments.Contains("--sandbox")) throw new InvalidDataException("Switching harness did not replace arguments with Codex defaults.");

            foreach (int width in new int[] { 60, 100 })
            foreach (Theme theme in new Theme[] { Theme.Dark, Theme.Light })
            {
                token.ThrowIfCancellationRequested();
                HeadlessBackend backend = new HeadlessBackend(width, 24) { ClaimsTerminal = false };
                using (TuiApplication app = new TuiApplication(backend))
                {
                    app.Theme = theme; app.Start();
                    RunnerProfile original = new RunnerProfile { Name = "Terminal 議論", Kind = "cli", Provider = "plain", Executable = "/fixture directory/agent", Arguments = new List<string> { "", "--directory", "space and Unicode 議論" } };
                    ParticipantEditor editor = new ParticipantEditor(original);
                    FormDialog<RunnerProfile> dialog = new FormDialog<RunnerProfile>("Edit participant", editor.Form, editor.Extract);
                    dialog.ApplyTheme(theme);
                    Task<RunnerProfile?> outcome = app.ShowAsync<RunnerProfile>(dialog);
                    editor.Form.SetFocusedField(1); backend.FeedKey("enter"); app.PumpInputOnce(); app.RenderOnce();
                    if (!((Dropdown<string>)editor.Form.GetField(1)).IsOpen) throw new InvalidDataException("The agent runner dropdown did not open.");
                    backend.FeedKey("escape");
                    await PumpUntilAsync(app, () => !((Dropdown<string>)editor.Form.GetField(1)).IsOpen, token).ConfigureAwait(false);
                    if (dialog.IsClosed) throw new InvalidDataException("Escape discarded the agent draft instead of closing its dropdown.");
                    backend.FeedKey("f1"); app.PumpInputOnce(); app.RenderOnce();
                    if (!Snapshot.ToText(app.CaptureFrame()!).Contains("Back to dialog", StringComparison.Ordinal)) throw new InvalidDataException("An agent form did not offer context help.");
                    backend.FeedKey("escape");
                    await PumpUntilAsync(app, () => !Snapshot.ToText(app.CaptureFrame()!).Contains("Back to dialog", StringComparison.Ordinal), token).ConfigureAwait(false);
                    app.RenderOnce();
                    string screen = Snapshot.ToText(app.CaptureFrame() ?? throw new InvalidDataException("Participant dialog frame was missing."));
                    await SaveEvidenceAsync("participants-" + width + "-" + theme.Name, screen, token).ConfigureAwait(false);
                    if (!screen.Contains("Edit participant", StringComparison.Ordinal) || !screen.Contains("Name", StringComparison.Ordinal)) throw new InvalidDataException("The participant form did not render its title and fields.");
                    editor.Form.SetFocusedField(editor.Form.FieldCount - 1);
                    backend.FeedKey("tab"); app.PumpInputOnce(); app.RenderOnce();
                    string bottom = Snapshot.ToText(app.CaptureFrame()!);
                    if (!bottom.Contains("Save", StringComparison.Ordinal) || !bottom.Contains("Cancel", StringComparison.Ordinal)) throw new InvalidDataException("Tab could not scroll the participant form's action buttons into view.");
                    await SaveEvidenceAsync("participants-bottom-" + width + "-" + theme.Name, bottom, token).ConfigureAwait(false);
                    ((TextField)editor.Form.GetField(3)).Value = "claude --dangerously-skip-permissions";
                    backend.FeedKey("ctrl+s"); app.PumpInputOnce(); app.RenderOnce();
                    if (dialog.IsClosed || !(editor.Form.GetFieldError(3) ?? "").Contains("separate Argument rows", StringComparison.Ordinal)) throw new InvalidDataException("The agent form accepted flags in the executable field without explaining the correction.");
                    await SaveEvidenceAsync("participant-command-error-" + width + "-" + theme.Name, Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                    ((TextField)editor.Form.GetField(3)).Value = original.Executable;
                    backend.FeedKey("ctrl+s"); app.PumpInputOnce();
                    if (!dialog.IsClosed) throw new InvalidDataException("Save did not close the valid participant form: " + string.Join("; ", Enumerable.Range(0, editor.Form.FieldCount).Select(index => editor.Form.GetFieldError(index)).Where(error => error != null)));
                    RunnerProfile? submitted = await outcome.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                    if (submitted == null || submitted.Executable != original.Executable || !submitted.Arguments.SequenceEqual(original.Arguments)) throw new InvalidDataException("Terminal form submission changed literal argument order or dropped an empty argument.");
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
                    ParticipantEditor blank = new ParticipantEditor(new RunnerProfile());
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
        private static async Task ShellAsync(CancellationToken token)
        {
            foreach (int width in new int[] { 60, 80, 120 })
            {
                token.ThrowIfCancellationRequested();
                HeadlessBackend backend = new HeadlessBackend(width, 24) { ClaimsTerminal = false };
                using (ArgueBotClient client = new ArgueBotClient())
                using (TuiApplication app = new TuiApplication(backend))
                {
                    new ConversationApp(client).Configure(app);
                    app.Start(); app.RenderOnce();
                    string screen = Snapshot.ToText(app.CaptureFrame() ?? throw new InvalidDataException("Terminal frame was missing."));
                    await SaveEvidenceAsync("workspace-" + width, screen, token).ConfigureAwait(false);
                    foreach (string label in new[] { "Conversations", "Note: Enter sends", "Welcome to ArgueBot", "Run controls", "Start debate" })
                        if (!screen.Contains(label, StringComparison.Ordinal)) throw new InvalidDataException("Workspace at " + width + " columns clipped " + label);
                    // The toolkit audit recognizes focus frames, not the borderless control indicators verified below.
                    FocusAudit.Run(app, new FocusAuditOptions { CheckFocusIndicator = false }).ThrowIfProblems();
                    app.Focus("actions"); app.RenderOnce();
                    CellBuffer actionFocus = app.CaptureFrame()!;
                    if (!Enumerable.Range(0, width * 4).Any(offset => actionFocus.Get(offset % width, offset / width).Style.Background.Equals(app.Theme.Selection.Background))) throw new InvalidDataException("Toolbar buttons lost their local focus highlight.");
                    foreach (string region in new[] { "threads", "composer" })
                    {
                        app.Focus(region); app.RenderOnce();
                        string focusedScreen = Snapshot.ToText(app.CaptureFrame()!);
                        if (!focusedScreen.Contains(region == "threads" ? "› Conversations" : "› Note: Enter sends", StringComparison.Ordinal)) throw new InvalidDataException("A borderless workspace control lost its focus indicator.");
                        if (!Snapshot.ToText(app.CaptureFrame()!).Split('\n').Last().Trim().StartsWith("F1 Help", StringComparison.Ordinal)) throw new InvalidDataException("Help lost the first footer position when focus moved.");
                    }
                    backend.FeedKey("ctrl+t"); app.PumpInputOnce(); app.RenderOnce();
                    if (app.Theme.Name != "Light") throw new InvalidDataException("Theme shortcut was not delivered.");
                    backend.FeedKey("ctrl+t"); app.PumpInputOnce(); app.RenderOnce();
                    if (app.Theme.Name != "Dark") throw new InvalidDataException("Theme shortcut did not toggle back.");
                    backend.Resize(40, 16); app.RenderOnce();
                    if (!Snapshot.ToText(app.CaptureFrame()!).Contains("Terminal too small", StringComparison.Ordinal)) throw new InvalidDataException("A small workspace silently clipped its controls.");
                    backend.Resize(width, 20); app.RenderOnce();
                    if (Snapshot.ToText(app.CaptureFrame()!).Contains("Terminal too small", StringComparison.Ordinal) || !app.EnableMouseRouting) throw new InvalidDataException("Resizing did not restore the workspace and mouse routing.");
                    app.Stop();
                }
            }
        }
    }
}
#endif
