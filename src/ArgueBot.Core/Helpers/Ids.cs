namespace ArgueBot.Core.Helpers
{
    /// <summary>Generates sortable application identifiers.</summary>
    public static class Ids
    {
        private static readonly PrettyId.IdGenerator _Generator = new PrettyId.IdGenerator();
        /// <summary>Generate a prefixed identifier up to 64 characters.</summary>
        public static string New(string prefix) { return _Generator.GenerateKSortable(prefix, 24); }
    }
}
