#if NET10_0_OR_GREATER
namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Tui;
    using TUIKit.Hosting;
    using TUIKit.Terminal;
    using TUIKit.Testing;
    using TUIKit.Theming;
    using Touchstone.Core;

    /// <summary>Headless tests render and drive the actual terminal application shell.</summary>
    public static class TerminalSuites
    {
        /// <summary>Terminal descriptors run on the executable's .NET 10 target.</summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get { return new List<TestSuiteDescriptor> { new TestSuiteDescriptor("Terminal", "Headless terminal", new List<TestCaseDescriptor> { new TestCaseDescriptor("Terminal", "Shell", "Responsive shell renders, traverses focus and toggles theme", ShellAsync) }) }; }
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
