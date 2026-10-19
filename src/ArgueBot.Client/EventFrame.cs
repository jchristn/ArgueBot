namespace ArgueBot.Client
{
    internal sealed class EventFrame
    {
        internal string Name { get; }
        internal string Id { get; }
        internal string Data { get; }
        internal EventFrame(string name, string id, string data) { Name = name; Id = id; Data = data; }
    }
}
