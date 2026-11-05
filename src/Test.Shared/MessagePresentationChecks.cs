namespace Test.Shared
{
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Shared transcript presentation used by the terminal: one card per finished tool call.</summary>
    internal static class MessagePresentationChecks
    {
        internal static Task RunAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            List<Message> messages = new List<Message> { Tool("m1", "started", "c1", "att_1"), Tool("m2", "started", "c2", "att_1"), Tool("m3", "completed", "c1", "att_1"), Tool("m4", "completed", "c1", "att_2"), new Message { Id = "m5", Kind = "answer", Content = "Status: started" } };
            List<string> visible = MessagePresentation.MergeToolActivity(messages).Select(message => message.Id).ToList();
            if (!visible.SequenceEqual(new[] { "m2", "m3", "m4", "m5" })) throw new InvalidDataException("Tool activity must show one card per finished call, keep unfinished calls, and never merge across attempts: " + string.Join(",", visible));
            Readable(new Message { Kind = "question", State = "completed", Content = "{\"question\": \"Which risk matters most?\", \"rationale\": \"It decides the rollout.\"}" }, "Which risk matters most?\n\nIt decides the rollout.", "A plain question reply");
            Readable(new Message { Kind = "synthesis", State = "completed", Content = "```json\n{\"proposal\": \"Ship **behind** a flag.\"}\n```" }, "Ship **behind** a flag.", "A reply inside a code fence");
            Readable(new Message { Kind = "ratification", State = "completed", Content = "```json\n{\"proposalId\": \"prp_1\", \"position\": \"needs_information\", \"rationale\": \"Missing data.\"}\n```" }, "**needs information**\n\nMissing data.", "A fenced vote");
            Readable(new Message { Kind = "synthesis", State = "completed", ContentTruncated = true, Content = "{\"proposal\": \"Line one\\nLine \\u0022two\\u0022 continues and is cut" }, "Line one\nLine \"two\" continues and is cut\u2026", "A preview cut off inside the proposal");
            Readable(new Message { Kind = "question", State = "streaming", Content = "```json\n{\"question\": \"Still wri" }, "Still wri\u2026", "A streaming question");
            Readable(new Message { Kind = "question", State = "completed", ContentTruncated = true, Content = "{\"question\": \"Complete question.\", \"rationale\": \"cut" }, "Complete question.", "A preview cut off after the question");
            Readable(new Message { Kind = "question", State = "failed", Content = "{\"oops" }, "{\"oops", "A failed turn keeps its raw text");
            Readable(new Message { Kind = "response", State = "completed", Content = "{\"question\": \"not an envelope\"}" }, "{\"question\": \"not an envelope\"}", "Ordinary responses are never rewritten");
            Readable(new Message { Kind = "question", State = "completed", Content = "Plain text question?" }, "Plain text question?", "A plain-text question stays as written");
            return Task.CompletedTask;
        }
        private static void Readable(Message message, string expected, string label)
        {
            string actual = MessagePresentation.Markdown(message);
            if (actual != expected) throw new InvalidDataException(label + " rendered as " + JsonSerializer.Serialize(actual) + " instead of " + JsonSerializer.Serialize(expected) + ".");
        }
        private static Message Tool(string id, string status, string call, string attempt)
        { return new Message { Id = id, Kind = "tool", Content = "Local tool: read_file\nStatus: " + status + "\nAttempt: " + attempt + "\nCall: " + call + "\nArguments:\npath: a.txt" }; }
    }
}
