namespace ArgueBot.Core.Services
{
    using ArgueBot.Core.Models;
    using System.Collections.Generic;

    internal sealed class PreparedContext
    {
        internal string Prompt { get; set; } = "";
        internal List<Message> Messages { get; set; } = new List<Message>();
    }
}
