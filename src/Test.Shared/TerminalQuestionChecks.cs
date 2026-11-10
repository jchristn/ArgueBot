#if NET10_0_OR_GREATER
namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Tui;
    using TUIKit.Hosting;
    using TUIKit.Terminal;
    using TUIKit.Testing;
    using TUIKit.Widgets;

    /// <summary>The terminal question dialogs against a real service: they open by themselves on the waiting conversation, answer through modal choices, Answer later parks the turn, and the Questions screen reopens it.</summary>
    internal static class TerminalQuestionChecks
    {
        internal static async Task RunAsync(CancellationToken token)
        {
            SetupFields();
            await ChecklistAsync(token).ConfigureAwait(false);
            string home = Path.Combine(Path.GetTempPath(), "arguebot-terminal-questions-" + Guid.NewGuid().ToString("N"));
            Process? service = null;
            try
            {
                TcpListener listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                await new ServiceSettings { Port = port }.SaveAsync(new ArgueBotPaths(home), token).ConfigureAwait(false);
                string endpoint = await LocalServiceConnector.ConnectAsync(home, token: token).ConfigureAwait(false);
                LocalServiceDescriptor descriptor = JsonSerializer.Deserialize<LocalServiceDescriptor>(await File.ReadAllTextAsync(Path.Combine(home, "runtime", "service.json"), token).ConfigureAwait(false)) ?? throw new InvalidDataException("No service descriptor.");
                service = Process.GetProcessById(descriptor.ProcessId);
                using (ArgueBotClient client = LocalServiceConnector.CreateClient(endpoint, home, IdentitySeed.Token))
                {
                    string simulation = Environment.GetEnvironmentVariable("ARGUEBOT_SIMULATION_URL")!;
                    RunnerProfile asker = await client.CreateParticipantAsync(new RunnerProfile { Name = "Terminal asker", Kind = "rest", Provider = "ollama", Endpoint = simulation, Model = "multi-agent-questions" }, token).ConfigureAwait(false);
                    RunnerProfile member = await client.CreateParticipantAsync(new RunnerProfile { Name = "Terminal panelist", Kind = "rest", Provider = "ollama", Endpoint = simulation, Model = "multi-agent" }, token).ConfigureAwait(false);
                    RunnerProfile orchestrator = await client.CreateParticipantAsync(new RunnerProfile { Name = "Terminal orchestrator", Kind = "rest", Provider = "ollama", Endpoint = simulation, Model = "multi-agent" }, token).ConfigureAwait(false);
                    Conversation conversation = await client.CreateConversationAsync("Terminal question journey", token).ConfigureAwait(false);
                    HeadlessBackend backend = new HeadlessBackend(100, 30) { ClaimsTerminal = false };
                    using (TuiApplication app = new TuiApplication(backend))
                    {
                        ConversationApp workspace = new ConversationApp(client); workspace.Configure(app); app.Start();
                        await PumpAsync(app, () => Snapshot.ToText(app.CaptureFrame()!).Contains("Terminal question journey", StringComparison.Ordinal), token, workspace).ConfigureAwait(false);

                        // The question dialogs open by themselves when the open conversation's debate waits for an answer.
                        DebateRun run = await client.StartAsync(conversation.Id, Request(asker, member, orchestrator, "terminal-answer"), token).ConfigureAwait(false);
                        await PumpAsync(app, () => app.Modals.Top is ChoiceDialog && Snapshot.ToText(app.CaptureFrame()!).Contains("Answer now", StringComparison.Ordinal), token, workspace).ConfigureAwait(false);
                        string intro = Snapshot.ToText(app.CaptureFrame()!);
                        if (!intro.Contains("Terminal asker asks", StringComparison.Ordinal) || !intro.Contains("Scope: Which", StringComparison.Ordinal) || !intro.Contains("Do not paste passwords", StringComparison.Ordinal)) throw new InvalidDataException("The question overview must name the asking agent and the question: " + intro);
                        await TerminalSuites.SaveEvidenceAsync("questions-overview", intro, token).ConfigureAwait(false);
                        TerminalSuites.ClickText(app, backend, "Answer now");
                        await PumpAsync(app, () => app.Modals.Top is ChoiceDialog && Snapshot.ToText(app.CaptureFrame()!).Contains("Production only (recommended)", StringComparison.Ordinal), token, workspace).ConfigureAwait(false);
                        await TerminalSuites.SaveEvidenceAsync("questions-choice", Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                        TerminalSuites.ClickText(app, backend, "Production only (recommended)");
                        await PumpAsync(app, () => app.Modals.Top is ChoiceDialog && Snapshot.ToText(app.CaptureFrame()!).Contains("Send these answers?", StringComparison.Ordinal), token, workspace).ConfigureAwait(false);
                        TerminalSuites.ClickText(app, backend, "Submit");
                        await PumpAsync(app, () => app.Modals.Top == null && !workspace.IsBusy, token, workspace).ConfigureAwait(false);
                        QuestionRequest answered = (await client.RunQuestionsAsync(conversation.Id, run.Id, token).ConfigureAwait(false)).Single();
                        if (answered.Status != "answered" || answered.Questions[0].Answer!.SelectedLabels.Single() != "Production only") throw new InvalidDataException("The terminal answer was not stored.");
                        run = await WaitAsync(client, conversation.Id, run, token).ConfigureAwait(false);

                        // Answer later parks the turn; the Questions screen reopens the question and the answer resumes the step.
                        Conversation second = await client.CreateConversationAsync("Terminal question later", token).ConfigureAwait(false);
                        await PumpAsync(app, () => Snapshot.ToText(app.CaptureFrame()!).Contains("Terminal question later", StringComparison.Ordinal), token, workspace).ConfigureAwait(false);
                        TerminalSuites.ClickText(app, backend, "Terminal question later");
                        await Task.Delay(300, token).ConfigureAwait(false);
                        DebateRun later = await client.StartAsync(second.Id, Request(asker, member, orchestrator, "terminal-later"), token).ConfigureAwait(false);
                        await PumpAsync(app, () => app.Modals.Top is ChoiceDialog && Snapshot.ToText(app.CaptureFrame()!).Contains("Answer now", StringComparison.Ordinal), token, workspace).ConfigureAwait(false);
                        TerminalSuites.ClickText(app, backend, "Answer later");
                        await PumpAsync(app, () => app.Modals.Top == null && !workspace.IsBusy, token, workspace).ConfigureAwait(false);
                        for (int attempt = 0; attempt < 200 && (await client.RunAsync(second.Id, later.Id, token).ConfigureAwait(false)).State != "awaiting_input"; attempt++) await Task.Delay(50, token).ConfigureAwait(false);
                        QuestionRequest parked = (await client.RunQuestionsAsync(second.Id, later.Id, token).ConfigureAwait(false)).Single();
                        if (parked.HoldState != "parked" || parked.Status != "open") throw new InvalidDataException("Answer later must park the open question: " + parked.HoldState);
                        await Task.Delay(2500, token).ConfigureAwait(false);
                        backend.FeedKey("f2");
                        await PumpAsync(app, () => app.Modals.Top is ChoiceDialog && Snapshot.ToText(app.CaptureFrame()!).Contains("Questions waiting for you (1)", StringComparison.Ordinal), token, workspace).ConfigureAwait(false);
                        TerminalSuites.ClickText(app, backend, "Questions waiting for you (1)");
                        await PumpAsync(app, () => app.Modals.Top is ChoiceDialog && Snapshot.ToText(app.CaptureFrame()!).Contains("Terminal asker: Scope", StringComparison.Ordinal), token, workspace).ConfigureAwait(false);
                        await TerminalSuites.SaveEvidenceAsync("questions-screen", Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                        TerminalSuites.ClickText(app, backend, "Terminal asker: Scope");
                        await PumpAsync(app, () => app.Modals.Top is ChoiceDialog && Snapshot.ToText(app.CaptureFrame()!).Contains("Answer now", StringComparison.Ordinal), token, workspace).ConfigureAwait(false);
                        TerminalSuites.ClickText(app, backend, "Answer now");
                        await PumpAsync(app, () => app.Modals.Top is ChoiceDialog && Snapshot.ToText(app.CaptureFrame()!).Contains("Every environment", StringComparison.Ordinal), token, workspace).ConfigureAwait(false);
                        TerminalSuites.ClickText(app, backend, "Every environment");
                        await PumpAsync(app, () => app.Modals.Top is ChoiceDialog && Snapshot.ToText(app.CaptureFrame()!).Contains("Send these answers?", StringComparison.Ordinal), token, workspace).ConfigureAwait(false);
                        TerminalSuites.ClickText(app, backend, "Submit");
                        await PumpAsync(app, () => app.Modals.Top is ChoiceDialog && Snapshot.ToText(app.CaptureFrame()!).Contains("No questions are waiting", StringComparison.Ordinal), token, workspace).ConfigureAwait(false);
                        backend.FeedKey("escape");
                        await PumpAsync(app, () => !workspace.IsBusy, token, workspace).ConfigureAwait(false);
                        later = await WaitAsync(client, second.Id, later, token).ConfigureAwait(false);
                        QuestionRequest resumed = (await client.RunQuestionsAsync(second.Id, later.Id, token).ConfigureAwait(false)).Single();
                        if (resumed.Questions[0].Answer!.SelectedLabels.Single() != "Every environment" || resumed.ResumeAttemptId.Length == 0) throw new InvalidDataException("The answer from the Questions screen must resume the parked step.");
                        app.Stop();
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
                await ServiceLifecycleSuites.DeleteStoppedHomeAsync(home).ConfigureAwait(false);
            }
        }

        /// <summary>Pump input and rendering until the condition holds, for up to twenty seconds: polling the service and waiting for an agent to ask take longer than a keypress.</summary>
        private static async Task PumpAsync(TuiApplication app, Func<bool> condition, CancellationToken token, ConversationApp? workspace = null)
        {
            DateTime limit = DateTime.UtcNow.AddSeconds(20);
            Task poll = Task.CompletedTask;
            while (DateTime.UtcNow < limit)
            {
                token.ThrowIfCancellationRequested(); app.PumpInputOnce(); app.RenderOnce();
                if (condition()) { while (!poll.IsCompleted) { app.PumpInputOnce(); app.RenderOnce(); await Task.Delay(10, token).ConfigureAwait(false); } return; }
                // Stand in for the TUI's half-second poll loop, which the headless harness does not run.
                if (workspace != null && poll.IsCompleted) { await poll.ConfigureAwait(false); poll = workspace.PollOnceAsync(token); }
                await Task.Delay(10, token).ConfigureAwait(false);
            }
            throw new InvalidDataException("The terminal question journey did not reach the expected screen. Current modal: " + (app.Modals.Top is TUIKit.Modals.DialogModal dialog ? dialog.Title : "none") + "\n" + Snapshot.ToText(app.CaptureFrame()!));
        }

        private static CreateRunRequest Request(RunnerProfile asker, RunnerProfile member, RunnerProfile orchestrator, string key)
        { return new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = orchestrator.Id, ParticipantIds = new List<string> { asker.Id, member.Id }, Question = "How should we roll out?", MaxRounds = 1, InterventionSeconds = 0, Policy = new DebatePolicy { Intake = "off", QuestionHoldSeconds = 120 }, IdempotencyKey = key }; }

        private static async Task<DebateRun> WaitAsync(ArgueBotClient client, string conversationId, DebateRun run, CancellationToken token)
        {
            for (int attempt = 0; attempt < 400; attempt++)
            {
                run = await client.RunAsync(conversationId, run.Id, token).ConfigureAwait(false);
                if (run.State == "completed") return run;
                if (run.State == "failed" || run.State == "paused" || run.State == "cancelled") break;
                await Task.Delay(50, token).ConfigureAwait(false);
            }
            throw new InvalidDataException("The debate did not finish after the terminal answer: " + run.State + "/" + run.Outcome);
        }

        /// <summary>The multi-select checklist toggles with Space, enforces its bounds and confirms with Enter.</summary>
        private static async Task ChecklistAsync(CancellationToken token)
        {
            HeadlessBackend backend = new HeadlessBackend(80, 24) { ClaimsTerminal = false };
            using (TuiApplication app = new TuiApplication(backend))
            {
                app.Start();
                ChecklistDialog dialog = new ChecklistDialog("Failures", "Which failures hurt? Choose 1 to 2.", new[] { "Migrations", "Configuration drift", "Capacity" }, 1, 2);
                Task<object?> shown = app.ShowAsync(dialog);
                await PumpAsync(app, () => app.Modals.Top is ChecklistDialog, token).ConfigureAwait(false);
                backend.FeedKey("enter");
                await PumpAsync(app, () => Snapshot.ToText(app.CaptureFrame()!).Contains("Choose at least 1.", StringComparison.Ordinal), token).ConfigureAwait(false);
                backend.FeedInput(new byte[] { (byte)' ' }); backend.FeedKey("down"); backend.FeedInput(new byte[] { (byte)' ' }); backend.FeedKey("down"); backend.FeedInput(new byte[] { (byte)' ' });
                await PumpAsync(app, () => Snapshot.ToText(app.CaptureFrame()!).Contains("Choose at most 2.", StringComparison.Ordinal), token).ConfigureAwait(false);
                await TerminalSuites.SaveEvidenceAsync("questions-checklist", Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                backend.FeedKey("enter");
                await PumpAsync(app, () => shown.IsCompleted, token).ConfigureAwait(false);
                int[] picked = (int[])(await shown.ConfigureAwait(false))!;
                if (!picked.SequenceEqual(new[] { 0, 1 })) throw new InvalidDataException("The checklist returned the wrong selection: " + string.Join(",", picked));
                app.Stop();
            }
        }

        /// <summary>Debate setup offers the question settings and sends them in the policy.</summary>
        private static void SetupFields()
        {
            List<RunnerProfile> profiles = new List<RunnerProfile> { new RunnerProfile { Id = "par_a", Name = "Agent A", Enabled = true }, new RunnerProfile { Id = "par_b", Name = "Agent B", Enabled = true } };
            DebateSetupEditor editor = new DebateSetupEditor(profiles, "Which rollout?", false);
            ((Dropdown<string>)editor.SpeakersForm.GetField(2)).SelectedIndex = 1;
            CreateRunRequest request = editor.Extract();
            if (request.Policy.UserQuestions != "all" || request.Policy.Intake != "ask_if_needed" || request.Policy.QuestionHoldSeconds != 300 || !request.Policy.ResumeOnAnswer || !editor.Review().Contains("Questions for you: all, opening questions if the question is vague, agents wait 300 seconds", StringComparison.Ordinal)) throw new InvalidDataException("Debate setup must default to the documented question settings: " + editor.Review());
            ((Dropdown<string>)editor.SettingsForm.GetField(20)).SelectedIndex = 1;
            ((Dropdown<string>)editor.SettingsForm.GetField(21)).SelectedIndex = 1;
            ((TextField)editor.SettingsForm.GetField(22)).Value = "60";
            ((Dropdown<string>)editor.SettingsForm.GetField(24)).SelectedIndex = 1;
            request = editor.Extract();
            if (request.Policy.UserQuestions != "orchestrator" || request.Policy.Intake != "off" || request.Policy.QuestionHoldSeconds != 60 || request.Policy.ResumeOnAnswer) throw new InvalidDataException("Debate setup must send the chosen question settings.");
        }
    }
}
#endif
