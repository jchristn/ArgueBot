namespace Test.Shared
{
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Encodings.Web;
    using System.Text.Json;
    using System.Threading.Tasks;
    using System.Threading;
    using System;
    using ArgueBot.Client;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using Touchstone.Core;

    /// <summary>Bounded history transport and stable membership semantics.</summary>
    public static class MessageSuites
    {
        /// <summary>Shared history descriptors for every adapter.</summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get { return new[] { new TestSuiteDescriptor("Messages", "Bounded history", new[] { new TestCaseDescriptor("Messages", "NativeEvents", "Native SSE validates UTF-8, cursor binding, duplication, gaps, revocation and bounded frames", NativeEventsAsync), new TestCaseDescriptor("Messages", "Replay", "Replay retention preserves monotonic anchors and transcript history; expired cursors fail visibly", ReplayAsync), new TestCaseDescriptor("Messages", "Paging", "Unicode previews, explicit full reads, stable paging during append and tenant isolation", PagingAsync) }) }; }
        }
        private static string Frame(long sequence, string conversation = "cnv_fixture")
        {
            string data = JsonSerializer.Serialize(new DurableEvent { ConversationId = conversation, Sequence = sequence, Kind = "delta", Text = "議論🙂" }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            return "id: " + conversation + ":" + sequence + "\r\nevent: change\r\ndata: " + data + "\r\n\r\n";
        }
        private static async Task NativeEventsAsync(CancellationToken token)
        {
            ReplayHttpHandler handler = new ReplayHttpHandler(Frame(1) + Frame(1) + Frame(2), 1);
            using (ArgueBotClient client = new ArgueBotClient("http://127.0.0.1:4567", handler))
            {
                List<DurableEvent> events = new List<DurableEvent>();
                await foreach (DurableEvent change in client.WatchEventsAsync("cnv_fixture", token: token).ConfigureAwait(false)) events.Add(change);
                if (handler.Cursor != "cnv_fixture:0" || events.Count != 2 || events[1].Sequence != 2 || events[0].Text != "議論🙂") throw new InvalidDataException("Native SSE lost split Unicode, duplicated an event or omitted cursor binding.");
            }
            await ExpectEventErrorAsync(new ReplayHttpHandler(Frame(3)), "event_history_gap", 1, token).ConfigureAwait(false);
            await ExpectEventErrorAsync(new ReplayHttpHandler(Frame(2, "cnv_foreign")), "invalid_event", 1, token).ConfigureAwait(false);
            await ExpectEventErrorAsync(new ReplayHttpHandler("event: error\ndata: {\"code\":\"event_history_gap\",\"message\":\"Refresh.\"}\n\n"), "event_history_gap", 1, token).ConfigureAwait(false);
            await ExpectEventErrorAsync(new ReplayHttpHandler("event: session_revoked\ndata: {}\n\n"), "unauthenticated", 1, token).ConfigureAwait(false);
            await ExpectEventErrorAsync(new ReplayHttpHandler("data: " + new string('x', ServiceProtocol.MaximumEventFrameCharacters)), "event_frame_too_large", 1, token).ConfigureAwait(false);
            await ExpectEventErrorAsync(new ReplayHttpHandler(new byte[] { 0xc3, 0x28 }), "invalid_event", 1, token).ConfigureAwait(false);
        }
        private static async Task ExpectEventErrorAsync(ReplayHttpHandler handler, string code, long after, CancellationToken token)
        {
            using (ArgueBotClient client = new ArgueBotClient("http://127.0.0.1:4567", handler))
            {
                bool denied = false;
                try { await foreach (DurableEvent change in client.WatchEventsAsync("cnv_fixture", after, token).ConfigureAwait(false)) { throw new InvalidDataException("An invalid SSE frame was applied."); } }
                catch (DomainException exception) when (exception.Code == code) { denied = true; }
                if (!denied) throw new InvalidDataException("Native SSE omitted its documented failure: " + code);
            }
        }
        private static async Task ReplayAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-replay-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    await ReplayChecks.RunAsync(database, new RequestContext { TenantId = "ten_local", UserId = "usr_local", Role = "admin", IsAuthenticated = true, IsAdmin = true }, token).ConfigureAwait(false);
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static async Task PagingAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-message-pages-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    await MessagePageChecks.RunAsync(database, new RequestContext { TenantId = "ten_local", UserId = "usr_local", Role = "admin", IsAuthenticated = true, IsAdmin = true }, token).ConfigureAwait(false);
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
    }
}
