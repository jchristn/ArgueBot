namespace Test.Shared
{
    using ArgueBot.Core.Models;

    /// <summary>One expected field-specific run admission failure.</summary>
    internal sealed class Rejection
    {
        internal string Name { get; }
        internal CreateRunRequest Request { get; }
        internal string Code { get; }
        internal string Field { get; }
        internal Rejection(string name, CreateRunRequest request, string code, string field) { Name = name; Request = request; Code = code; Field = field; }
    }
}
