namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using Touchstone.Core;

    /// <summary>Bounded history transport and stable membership semantics.</summary>
    public static class MessageSuites
    {
        /// <summary>Shared history descriptors for every adapter.</summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get { return new[] { new TestSuiteDescriptor("Messages", "Bounded history", new[] { new TestCaseDescriptor("Messages", "Paging", "Unicode previews, explicit full reads, stable paging during append and tenant isolation", PagingAsync) }) }; }
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
