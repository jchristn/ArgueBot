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
    using System.Text;
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

    /// <summary>The terminal Resources screen against a real service: list, edit in the text editor, lock, history with diff and revert; and resource access in debate setup.</summary>
    internal static class TerminalResourceChecks
    {
        internal static async Task RunAsync(CancellationToken token)
        {
            SetupDefaults();
            string home = Path.Combine(Path.GetTempPath(), "arguebot-terminal-resources-" + Guid.NewGuid().ToString("N"));
            Process? service = null;
            try
            {
                TcpListener listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                await new ServiceSettings { Port = port }.SaveAsync(new ArgueBotPaths(home), token).ConfigureAwait(false);
                string endpoint = await LocalServiceConnector.ConnectAsync(home, token: token).ConfigureAwait(false);
                LocalServiceDescriptor descriptor = JsonSerializer.Deserialize<LocalServiceDescriptor>(await File.ReadAllTextAsync(Path.Combine(home, "runtime", "service.json"), token).ConfigureAwait(false)) ?? throw new InvalidDataException("No fixture service descriptor.");
                service = Process.GetProcessById(descriptor.ProcessId);
                using (ArgueBotClient client = LocalServiceConnector.CreateClient(endpoint, home, IdentitySeed.Token))
                {
                    Resource billing = await client.CreateResourceAsync(new ResourceRequest { Name = "billing", Title = "Billing", Description = "How invoices work.", Body = "Line one." }, token).ConfigureAwait(false);
                    billing = await client.UpdateResourceAsync(billing.Id, new ResourceRequest { Body = "Line one.\nLine two.", Revision = 1 }, token).ConfigureAwait(false);
                    foreach (int width in new[] { 60, 100 })
                    {
                        HeadlessBackend backend = new HeadlessBackend(width, 30) { ClaimsTerminal = false };
                        using (TuiApplication app = new TuiApplication(backend))
                        {
                            ConversationApp workspace = new ConversationApp(client); workspace.Configure(app); app.Start();
                            backend.FeedKey("f2");
                            await TerminalSuites.PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog, token).ConfigureAwait(false);
                            TerminalSuites.ClickText(app, backend, "Resources: documents agents");
                            await TerminalSuites.PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog && Snapshot.ToText(app.CaptureFrame()!).Contains("billing: Billing", StringComparison.Ordinal), token).ConfigureAwait(false);
                            await TerminalSuites.SaveEvidenceAsync("resources-list-" + width, Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                            if (width == 60) { backend.FeedKey("escape"); await TerminalSuites.PumpUntilAsync(app, () => !workspace.IsBusy, token).ConfigureAwait(false); app.Stop(); continue; }

                            // Edit the text in the editor and save with Ctrl+S.
                            TerminalSuites.ClickText(app, backend, "billing: Billing");
                            await TerminalSuites.PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog && Snapshot.ToText(app.CaptureFrame()!).Contains("Edit text", StringComparison.Ordinal), token).ConfigureAwait(false);
                            TerminalSuites.ClickText(app, backend, "Edit text");
                            await TerminalSuites.PumpUntilAsync(app, () => app.Modals.Top is TextEditorDialog, token).ConfigureAwait(false);
                            backend.FeedKey("enter"); backend.FeedInput(Encoding.UTF8.GetBytes("Line three."));
                            await TerminalSuites.PumpUntilAsync(app, () => ((TextEditorDialog)app.Modals.Top!).Text.EndsWith("Line three.", StringComparison.Ordinal), token).ConfigureAwait(false);
                            await TerminalSuites.SaveEvidenceAsync("resources-editor", Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                            backend.FeedKey("ctrl+s");
                            await TerminalSuites.PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog && Snapshot.ToText(app.CaptureFrame()!).Contains("revision 3", StringComparison.Ordinal), token).ConfigureAwait(false);
                            Resource edited = await client.ResourceAsync("billing", token).ConfigureAwait(false);
                            if (edited.Body != "Line one.\nLine two.\nLine three." || edited.Revision != 3) throw new InvalidDataException("The terminal editor did not save the edited text: " + JsonSerializer.Serialize(edited.Body));

                            // Lock it.
                            TerminalSuites.ClickText(app, backend, "Lock: agents");
                            await TerminalSuites.PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog && Snapshot.ToText(app.CaptureFrame()!).Contains("Unlock: let agents", StringComparison.Ordinal), token).ConfigureAwait(false);
                            if (!(await client.ResourceAsync("billing", token).ConfigureAwait(false)).Locked) throw new InvalidDataException("The terminal lock action did not lock the resource.");

                            // History: open revision 1, see the diff and restore it.
                            TerminalSuites.ClickText(app, backend, "History, changes and revert");
                            await TerminalSuites.PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog && Snapshot.ToText(app.CaptureFrame()!).Contains("Revision 1: create", StringComparison.Ordinal), token).ConfigureAwait(false);
                            await TerminalSuites.SaveEvidenceAsync("resources-history", Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                            TerminalSuites.ClickText(app, backend, "Revision 1: create");
                            await TerminalSuites.PumpUntilAsync(app, () => app.Modals.Top is DiffDialog, token).ConfigureAwait(false);
                            DiffDialog diff = (DiffDialog)app.Modals.Top!;
                            if (diff.AddedCount != 2 || diff.RemovedCount != 0) throw new InvalidDataException("The terminal diff did not show the two lines added since revision 1.");
                            await TerminalSuites.SaveEvidenceAsync("resources-diff", Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                            TerminalSuites.ClickText(app, backend, "Restore revision 1");
                            await TerminalSuites.PumpUntilAsync(app, () => app.Modals.Top is MessageDialog, token).ConfigureAwait(false);
                            Resource restored = await client.ResourceAsync("billing", token).ConfigureAwait(false);
                            if (restored.Body != "Line one." || restored.Revision != 5 || !restored.Locked) throw new InvalidDataException("Restoring from the terminal did not save revision 1's text as a new revision.");
                            backend.FeedKey("escape");
                            await TerminalSuites.PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog, token).ConfigureAwait(false);
                            backend.FeedKey("escape"); await TerminalSuites.PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog && Snapshot.ToText(app.CaptureFrame()!).Contains("Add a resource", StringComparison.Ordinal), token).ConfigureAwait(false);
                            backend.FeedKey("escape"); await TerminalSuites.PumpUntilAsync(app, () => !workspace.IsBusy, token).ConfigureAwait(false);
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
                await ServiceLifecycleSuites.DeleteStoppedHomeAsync(home).ConfigureAwait(false);
            }
        }

        /// <summary>Debate setup offers resource access, defaulting to read, and sends the chosen visibility and limits.</summary>
        private static void SetupDefaults()
        {
            List<RunnerProfile> profiles = new List<RunnerProfile> { new RunnerProfile { Id = "par_a", Name = "Agent A", Enabled = true }, new RunnerProfile { Id = "par_b", Name = "Agent B", Enabled = true } };
            DebateSetupEditor editor = new DebateSetupEditor(profiles, "Which rollout?", false);
            ((Dropdown<string>)editor.SpeakersForm.GetField(2)).SelectedIndex = 1;
            CreateRunRequest request = editor.Extract();
            if (request.Policy.ResourceAccess != "read" || request.Policy.ResourceNames.Count != 0 || request.Policy.MaxResourceWritesPerTurn != 8 || !editor.Review().Contains("Resources: read, every resource.", StringComparison.Ordinal)) throw new InvalidDataException("Debate setup must default to read access for every resource.");
            Dropdown<string> access = (Dropdown<string>)editor.SettingsForm.GetField(13);
            access.SelectedIndex = 1;
            ((TextField)editor.SettingsForm.GetField(14)).Value = "Billing, team/conventions";
            ((TextField)editor.SettingsForm.GetField(15)).Value = "finance";
            ((TextField)editor.SettingsForm.GetField(16)).Value = "3";
            request = editor.Extract();
            if (request.Policy.ResourceAccess != "read_write" || !request.Policy.ResourceNames.SequenceEqual(new[] { "billing", "team/conventions" }) || !request.Policy.ResourceTags.SequenceEqual(new[] { "finance" }) || request.Policy.MaxResourceWritesPerTurn != 3) throw new InvalidDataException("Debate setup must send the chosen resource access, visibility and limits.");
            if (!editor.Review().Contains("Resources: read_write, only billing, team/conventions, tag finance.", StringComparison.Ordinal)) throw new InvalidDataException("Review must summarize resource access: " + editor.Review());
        }
    }
}
#endif
