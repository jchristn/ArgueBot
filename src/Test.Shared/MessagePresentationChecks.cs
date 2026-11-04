namespace Test.Shared
{
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
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
            return Task.CompletedTask;
        }
        private static Message Tool(string id, string status, string call, string attempt)
        { return new Message { Id = id, Kind = "tool", Content = "Local tool: read_file\nStatus: " + status + "\nAttempt: " + attempt + "\nCall: " + call + "\nArguments:\npath: a.txt" }; }
    }
}
