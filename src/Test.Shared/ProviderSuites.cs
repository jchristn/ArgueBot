namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using Touchstone.Core;

    /// <summary>Real database provider contract tests against explicitly configured disposable databases.</summary>
    public static class ProviderSuites
    {
        /// <summary>Provider-matrix descriptors. Missing service credentials are reported as skips.</summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get
            {
                List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
                foreach (DatabaseTypeEnum provider in new[] { DatabaseTypeEnum.Mysql, DatabaseTypeEnum.Postgresql, DatabaseTypeEnum.SqlServer })
                {
                    string variable = "ARGUEBOT_TEST_" + provider.ToString().ToUpperInvariant();
                    bool missing = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable));
                    cases.Add(new TestCaseDescriptor("Providers", provider.ToString(), provider + " relational lifecycle and ownership", token => RunAsync(provider, variable, token), skip: missing, skipReason: missing ? "Set " + variable + " for a disposable test database." : null));
                }
                return new List<TestSuiteDescriptor> { new TestSuiteDescriptor("Providers", "Database provider contracts", cases) };
            }
        }
        private static async Task RunAsync(DatabaseTypeEnum provider, string variable, CancellationToken token)
        {
            DatabaseSettings settings = new DatabaseSettings { Type = provider, ConnectionEnvironmentVariable = variable };
            using (DatabaseDriverBase database = await DatabaseDriverFactory.CreateAndInitializeAsync(settings, token).ConfigureAwait(false))
            {
                await database.InitializeAsync(token).ConfigureAwait(false);
                RequestContext context = new RequestContext { TenantId = "ten_local", UserId = "usr_local", Role = "admin", IsAuthenticated = true };
                Conversation conversation = await database.CreateConversationAsync(context, "Provider contract 議論 ' % _ " + Guid.NewGuid().ToString("N"), token).ConfigureAwait(false);
                try
                {
                    Message message = await database.AppendMessageAsync(context, new Message { ConversationId = conversation.Id, Content = "provider-needle 議論" }, "unique", token).ConfigureAwait(false);
                    Message duplicate = await database.AppendMessageAsync(context, new Message { ConversationId = conversation.Id, Content = "provider-needle 議論" }, "unique", token).ConfigureAwait(false);
                    if (message.Id != duplicate.Id) throw new InvalidDataException("Receipt did not deduplicate.");
                    List<Message> messages = await database.ReadMessagesAsync(context, conversation.Id, token).ConfigureAwait(false);
                    if (messages.Single().Content != message.Content) throw new InvalidDataException("Provider corrupted message content.");
                    Page page = await database.EnumerateConversationsAsync(context, conversation.Title, token: token).ConfigureAwait(false);
                    if (page.TotalCount != 1) throw new InvalidDataException("Provider search/paging failed.");
                    Conversation updated = await database.ReadConversationAsync(context, conversation.Id, token).ConfigureAwait(false);
                    updated = await database.UpdateConversationAsync(context, conversation.Id, new ConversationRequest { Title = updated.Title, Pinned = true, Revision = updated.Revision }, token).ConfigureAwait(false);
                    if (!updated.Pinned) throw new InvalidDataException("Provider update failed.");
                    RequestContext foreign = new RequestContext { TenantId = "ten_foreign", UserId = "usr_local", IsAuthenticated = true, Role = "admin" };
                    bool denied = false;
                    try { await database.ReadConversationAsync(foreign, conversation.Id, token).ConfigureAwait(false); }
                    catch (DomainException ex) when (ex.StatusCode == 404) { denied = true; }
                    if (!denied) throw new InvalidDataException("Provider leaked foreign tenant history.");
                    List<DurableEvent> events = await database.ReadEventsAsync(context, conversation.Id, token: token).ConfigureAwait(false);
                    if (events.Count != 3 || events[2].Sequence != 3) throw new InvalidDataException("Provider event ordering failed.");
                }
                finally { await database.DeleteConversationAsync(context, conversation.Id, CancellationToken.None).ConfigureAwait(false); }
            }
        }
    }
}
