namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;

    /// <summary>Resource library storage: names, sizes, revisions, agent restrictions, history, import, search, tenant isolation and cascading tenant deletion.</summary>
    internal static class ResourceChecks
    {
        internal static async Task SqliteAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-resources-" + Guid.NewGuid().ToString("N"));
            try
            {
                FrontMatter();
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    await RunAsync(database, token).ConfigureAwait(false);
                }
                using (SqliteDatabaseDriver reopened = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await reopened.InitializeAsync(token).ConfigureAwait(false);
                    ResourceSummary summary = (await reopened.Resources.SummariesAsync(System(), token).ConfigureAwait(false)).Single(item => item.Name.StartsWith("team/", StringComparison.Ordinal));
                    Resource kept = await reopened.Resources.ReadAsync(System(), summary.Id, token).ConfigureAwait(false);
                    Require(kept.Revision > 1 && kept.Body.Contains("Use tabs.", StringComparison.Ordinal), "Resources and their revisions must survive a restart.");
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
            await UpgradeAsync(token).ConfigureAwait(false);
        }

        /// <summary>A schema 32 database upgrades to 33: resource tables appear, and debates that already exist keep resource access off.</summary>
        private static async Task UpgradeAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-resources-upgrade-" + Guid.NewGuid().ToString("N"));
            ArgueBotPaths paths = new ArgueBotPaths(home);
            RequestContext context = System();
            try
            {
                string conversationId, runId;
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    List<string> seats = new List<string>();
                    for (int index = 0; index < 3; index++) seats.Add((await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Upgrade agent " + index, Executable = "fictional", WorkingDirectory = home }, token).ConfigureAwait(false)).Id);
                    Conversation conversation = await database.CreateConversationAsync(context, "Before resources", token).ConfigureAwait(false);
                    DebateRun run = await database.Runs.CreateAsync(context, conversation.Id, new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = seats[0], ParticipantIds = seats.Skip(1).ToList(), Question = "Upgrade?", MaxRounds = 1, InterventionSeconds = 0, IdempotencyKey = "upgrade" }, token).ConfigureAwait(false);
                    conversationId = conversation.Id; runId = run.Id;
                    Require(run.Policy.ResourceAccess == "read", "New debates must default to read access.");
                }
                // Turn the database back into schema 32.
                using (Microsoft.Data.Sqlite.SqliteConnection connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + paths.DatabaseFile))
                {
                    await connection.OpenAsync(token).ConfigureAwait(false);
                    using (Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand())
                    {
                        command.CommandText = "DROP TABLE attempt_resources; DROP TABLE resource_revisions; DROP TABLE resources; " + string.Concat(new[] { "resource_access", "resource_names_json", "resource_tags_json", "max_resource_writes_per_turn", "max_resource_write_bytes_per_turn", "max_resources_created", "resources_created", "max_step_retries" }.Select(column => "ALTER TABLE debate_policies DROP COLUMN " + column + "; ")) + "DELETE FROM schema_migrations WHERE version>=33;";
                        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                using (SqliteDatabaseDriver upgraded = new SqliteDatabaseDriver(paths))
                {
                    await upgraded.InitializeAsync(token).ConfigureAwait(false);
                    DebateRun existing = await upgraded.Runs.ReadAsync(context, conversationId, runId, token).ConfigureAwait(false);
                    Require(existing.Policy.ResourceAccess == "off" && existing.Policy.MaxResourceWritesPerTurn == 8 && existing.Policy.MaxStepRetries == 1, "A debate created before schema 33 must keep resource access off, and its single correction request, after the upgrade.");
                    Resource created = await upgraded.Resources.CreateAsync(context, new ResourceRequest { Name = "after-upgrade", Body = "Works." }, token: token).ConfigureAwait(false);
                    Require(created.Revision == 1 && (await upgraded.Resources.RevisionsAsync(context, created.Id, token).ConfigureAwait(false)).Count == 1, "Resources must work after the upgrade.");
                }
            }
            finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(home)) Directory.Delete(home, true); }
        }

        internal static async Task RunAsync(DatabaseDriverBase database, CancellationToken token)
        {
            RequestContext system = System();
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);

            // Names are lowercased, unique without regard to case and follow the path-like rules.
            Resource billing = await database.Resources.CreateAsync(system, new ResourceRequest { Name = "Billing-" + suffix, Title = "Billing", Description = "How invoices work.", Tags = new List<string> { "Finance-" + suffix, "finance-" + suffix, "core" }, Body = "# Billing\nInvoices go out on the 1st, batch " + suffix + "." }, token: token).ConfigureAwait(false);
            Require(billing.Name == "billing-" + suffix && billing.Revision == 1 && billing.Id.StartsWith("res_", StringComparison.Ordinal), "A new resource must be stored with a lowercase name at revision 1.");
            Require(billing.Tags.SequenceEqual(new[] { "finance-" + suffix, "core" }) && billing.UpdatedByKind == "user" && billing.UpdatedById == "usr_local" && billing.Bytes == billing.Body.Length, "Tags must be lowercase without duplicates, and the creator must be recorded.");
            Require((await database.Resources.ReadByNameAsync(system, "BILLING-" + suffix, token).ConfigureAwait(false)).Id == billing.Id, "Names must be read case-insensitively.");
            await Reject(() => database.Resources.CreateAsync(system, new ResourceRequest { Name = "billing-" + suffix.ToUpperInvariant() }, token: token), "resource_exists", "Names must be unique without regard to case.").ConfigureAwait(false);
            foreach (string name in new[] { "", " ", "/billing", "billing/", "a//b", "a/../b", "a/./b", ".hidden", "-dash", "has space", "emoji☃", new string('a', 129) })
                await Reject(() => database.Resources.CreateAsync(system, new ResourceRequest { Name = name }, token: token), "invalid_resource_name", "The name '" + name + "' must be rejected.").ConfigureAwait(false);
            Resource conventions = await database.Resources.CreateAsync(system, new ResourceRequest { Name = "team/" + suffix + "/conventions", Body = "Use spaces." }, token: token).ConfigureAwait(false);
            Require(conventions.Name == "team/" + suffix + "/conventions" && conventions.Title.Length == 0, "Path-like names and empty titles must be accepted.");

            // Sizes and characters.
            await database.Resources.CreateAsync(system, new ResourceRequest { Name = "big-" + suffix, Body = new string('x', ResourceRules.MaxBodyBytes) }, token: token).ConfigureAwait(false);
            await Reject(() => database.Resources.CreateAsync(system, new ResourceRequest { Name = "too-big-" + suffix, Body = new string('x', ResourceRules.MaxBodyBytes + 1) }, token: token), "resource_too_large", "A body over 256 KiB must be rejected.").ConfigureAwait(false);
            await Reject(() => database.Resources.CreateAsync(system, new ResourceRequest { Name = "multibyte-" + suffix, Body = new string('é', ResourceRules.MaxBodyBytes / 2 + 1) }, token: token), "resource_too_large", "The size limit must count UTF-8 bytes, not characters.").ConfigureAwait(false);
            await Reject(() => database.Resources.CreateAsync(system, new ResourceRequest { Name = "nul-" + suffix, Body = "a\0b" }, token: token), "invalid_resource_body", "NUL characters must be rejected.").ConfigureAwait(false);
            await Reject(() => database.Resources.CreateAsync(system, new ResourceRequest { Name = "title-" + suffix, Title = "Two\nlines" }, token: token), "invalid_resource_title", "Titles must be one line.").ConfigureAwait(false);
            await Reject(() => database.Resources.CreateAsync(system, new ResourceRequest { Name = "tags-" + suffix, Tags = Enumerable.Range(0, 17).Select(index => "t" + index).ToList() }, token: token), "invalid_resource_tags", "At most 16 tags are allowed.").ConfigureAwait(false);

            // Updates need the current revision; null fields keep their values; renames are revisions too.
            await Reject(() => database.Resources.UpdateAsync(system, conventions.Id, new ResourceRequest { Body = "Use tabs.", Revision = 7 }, token: token), "revision_conflict", "A stale revision must be refused.").ConfigureAwait(false);
            Resource updated = await database.Resources.UpdateAsync(system, conventions.Id, new ResourceRequest { Body = "Use tabs.", Revision = 1 }, token: token).ConfigureAwait(false);
            Require(updated.Revision == 2 && updated.Body == "Use tabs." && updated.Name == "team/" + suffix + "/conventions", "An update must keep omitted fields and advance the revision.");
            await Reject(() => database.Resources.UpdateAsync(system, conventions.Id, new ResourceRequest { Name = "BILLING-" + suffix, Revision = 2 }, token: token), "resource_exists", "A rename onto another resource's name must be refused.").ConfigureAwait(false);
            Resource renamed = await database.Resources.UpdateAsync(system, billing.Id, new ResourceRequest { Name = "finance/billing-" + suffix, Revision = 1 }, token: token).ConfigureAwait(false);
            Require(renamed.Name == "finance/billing-" + suffix && renamed.Id == billing.Id, "A rename must keep the resource ID.");
            await Reject(() => database.Resources.ReadByNameAsync(system, "billing-" + suffix, token), "resource_not_found", "The old name must stop resolving after a rename.").ConfigureAwait(false);

            // Agents: attributed, never change locked resources, never lock, rename or create locked resources.
            ResourceAuthor agent = new ResourceAuthor { SeatId = "par_fixture_seat", Name = "Fixture agent", RunId = "run_fixture", AttemptId = "att_fixture" };
            Resource notes = await database.Resources.CreateAsync(system, new ResourceRequest { Name = "notes-" + suffix, Body = "First." }, agent, token).ConfigureAwait(false);
            Require(notes.UpdatedByKind == "agent" && notes.UpdatedById == "par_fixture_seat" && notes.UpdatedByName == "Fixture agent" && notes.CreatedByUserId == "usr_local", "Agent-created resources must be marked as agent-authored.");
            Resource appended = await database.Resources.AppendAsync(system, notes.Id, "Second.", agent, token).ConfigureAwait(false);
            Require(appended.Body == "First.\nSecond." && appended.Revision == 2, "Append must add a separating newline and a revision.");
            ResourceRevision agentRevision = await database.Resources.ReadRevisionAsync(system, notes.Id, 2, token).ConfigureAwait(false);
            Require(agentRevision.AuthorKind == "agent" && agentRevision.RunId == "run_fixture" && agentRevision.AttemptId == "att_fixture" && agentRevision.Change == "append" && agentRevision.Body == appended.Body, "Agent revisions must record the run and attempt.");
            await Reject(() => database.Resources.UpdateAsync(system, notes.Id, new ResourceRequest { Locked = true, Revision = 2 }, agent, token), "resource_lock_denied", "Agents must not lock resources.").ConfigureAwait(false);
            await Reject(() => database.Resources.UpdateAsync(system, notes.Id, new ResourceRequest { Name = "renamed-" + suffix, Revision = 2 }, agent, token), "resource_rename_denied", "Agents must not rename resources.").ConfigureAwait(false);
            await Reject(() => database.Resources.CreateAsync(system, new ResourceRequest { Name = "locked-" + suffix, Locked = true }, agent, token), "resource_lock_denied", "Agents must not create locked resources.").ConfigureAwait(false);
            Resource locked = await database.Resources.UpdateAsync(system, notes.Id, new ResourceRequest { Locked = true, Revision = 2 }, token: token).ConfigureAwait(false);
            Require(locked.Locked && locked.UpdatedByKind == "user", "Users must be able to lock a resource.");
            await Reject(() => database.Resources.UpdateAsync(system, notes.Id, new ResourceRequest { Body = "Overwritten.", Revision = 3 }, agent, token), "resource_locked", "Agents must not update locked resources.").ConfigureAwait(false);
            await Reject(() => database.Resources.AppendAsync(system, notes.Id, "More.", agent, token), "resource_locked", "Agents must not append to locked resources.").ConfigureAwait(false);
            Resource userEdit = await database.Resources.UpdateAsync(system, notes.Id, new ResourceRequest { Body = "Reviewed.", Revision = 3 }, token: token).ConfigureAwait(false);
            Require(userEdit.Body == "Reviewed." && userEdit.Locked, "Users must still edit locked resources.");

            // History: newest first without bodies, revert restores content as a new revision.
            List<ResourceRevision> history = await database.Resources.RevisionsAsync(system, notes.Id, token).ConfigureAwait(false);
            Require(history.Select(item => item.Revision).SequenceEqual(new long[] { 4, 3, 2, 1 }) && history.All(item => item.Body.Length == 0) && history[1].Change == "lock" && history[3].Change == "create", "History must list every revision newest first without bodies.");
            await Reject(() => database.Resources.RevertAsync(system, notes.Id, new ResourceRevertRequest { TargetRevision = 1, Revision = 3 }, token), "revision_conflict", "Revert must check the current revision.").ConfigureAwait(false);
            Resource reverted = await database.Resources.RevertAsync(system, notes.Id, new ResourceRevertRequest { TargetRevision = 1, Revision = 4 }, token).ConfigureAwait(false);
            Require(reverted.Body == "First." && reverted.Revision == 5 && reverted.Locked && reverted.Name == "notes-" + suffix, "Revert must restore the body as a new revision and keep the name and lock.");
            await Reject(() => database.Resources.ReadRevisionAsync(system, notes.Id, 99, token), "revision_not_found", "Unknown revisions must be reported.").ConfigureAwait(false);

            // The history cap keeps the newest revisions.
            Resource log = await database.Resources.CreateAsync(system, new ResourceRequest { Name = "log-" + suffix }, token: token).ConfigureAwait(false);
            for (int index = 0; index < ResourceRules.MaxRevisionsKept + 5; index++) log = await database.Resources.AppendAsync(system, log.Id, "entry " + index, token: token).ConfigureAwait(false);
            List<ResourceRevision> capped = await database.Resources.RevisionsAsync(system, log.Id, token).ConfigureAwait(false);
            Require(capped.Count == ResourceRules.MaxRevisionsKept && capped[0].Revision == log.Revision && capped[capped.Count - 1].Revision == log.Revision - ResourceRules.MaxRevisionsKept + 1, "History must keep exactly the newest " + ResourceRules.MaxRevisionsKept + " revisions.");

            // Listing, filters and search.
            ResourcePage page = await database.Resources.EnumerateAsync(system, tag: "FINANCE-" + suffix.ToUpperInvariant(), token: token).ConfigureAwait(false);
            Require(page.TotalCount == 1 && page.Items[0].Id == billing.Id, "Tag filters must match without regard to case.");
            ResourcePage searched = await database.Resources.EnumerateAsync(system, search: "invoices WORK", token: token).ConfigureAwait(false);
            Require(searched.Items.Any(item => item.Id == billing.Id), "List search must match descriptions without regard to case.");
            ResourcePage paged = await database.Resources.EnumerateAsync(system, pageNumber: 2, pageSize: 1, token: token).ConfigureAwait(false);
            Require(paged.Items.Count == 1 && paged.TotalCount >= 5 && paged.PageNumber == 2, "Lists must page.");
            List<ResourceSearchHit> hits = await database.Resources.SearchAsync(system, "INVOICES GO OUT ON THE 1ST, BATCH " + suffix.ToUpperInvariant(), token: token).ConfigureAwait(false);
            Require(hits.Count == 1 && hits[0].Field == "body" && hits[0].MatchOffset == 10 && hits[0].Snippet.Contains("Invoices go out", StringComparison.Ordinal), "Body search must be literal and case-insensitive with a snippet.");
            Require((await database.Resources.SearchAsync(system, "invoices", new List<string> { conventions.Id }, token: token).ConfigureAwait(false)).Count == 0, "Search must honor the visible resource list.");
            Require((await database.Resources.SearchAsync(system, "%", token: token).ConfigureAwait(false)).Count == 0, "Search must treat wildcard characters literally.");

            // Import and export.
            ResourceImportResult imported = await database.Resources.ImportAsync(system, new ResourceImportRequest { Files = new List<ResourceImportFile> { new ResourceImportFile { FileName = "Release Notes " + suffix + ".md", Content = "No front matter." }, new ResourceImportFile { FileName = "x.md", Content = "---\nname: imported/" + suffix + "\ntitle: \"Imported: one\"\ntags:\n  - alpha\n  - Beta\n---\n\nBody text." } } }, token).ConfigureAwait(false);
            Require(imported.Created == 2 && imported.Items.Count == 2 && imported.Items[0].Name == "release-notes-" + suffix && imported.Items[1].Tags.SequenceEqual(new[] { "alpha", "beta" }), "Import must name files from front matter or the file name.");
            Resource importedBody = await database.Resources.ReadByNameAsync(system, "imported/" + suffix, token).ConfigureAwait(false);
            Require(importedBody.Body == "Body text." && importedBody.Title == "Imported: one", "Import must strip the front matter from the body.");
            await Reject(() => database.Resources.ImportAsync(system, new ResourceImportRequest { Files = new List<ResourceImportFile> { new ResourceImportFile { FileName = "fresh-" + suffix + ".md", Content = "new" }, new ResourceImportFile { FileName = "x.md", Content = "---\nname: imported/" + suffix + "\n---\nreplaced" } } }, token), "resource_exists", "Import must refuse existing names without overwrite.").ConfigureAwait(false);
            await Reject(() => database.Resources.ReadByNameAsync(system, "fresh-" + suffix, token), "resource_not_found", "A refused import must not keep any of its files.").ConfigureAwait(false);
            ResourceImportResult replaced = await database.Resources.ImportAsync(system, new ResourceImportRequest { Overwrite = true, Files = new List<ResourceImportFile> { new ResourceImportFile { FileName = "x.md", Content = ResourceMarkdown.Format(importedBody).Replace("Body text.", "Replaced text.", StringComparison.Ordinal) } } }, token).ConfigureAwait(false);
            Resource afterReplace = await database.Resources.ReadByNameAsync(system, "imported/" + suffix, token).ConfigureAwait(false);
            Require(replaced.Updated == 1 && afterReplace.Body == "Replaced text." && afterReplace.Revision == 2 && afterReplace.Title == "Imported: one", "Overwrite must replace the content as a new revision.");

            // Tenant isolation and cascading tenant deletion.
            Tenant other = await database.Identity.CreateTenantAsync(system, new TenantRequest { Name = "Resource tenant " + suffix }, token).ConfigureAwait(false);
            RequestContext outsider = new RequestContext { TenantId = other.Id, UserId = "usr_resource_outsider", IsAuthenticated = true, IsTenantAdmin = true, Role = "member" };
            await Reject(() => database.Resources.ReadAsync(outsider, conventions.Id, token), "resource_not_found", "Another tenant must not read a resource by ID.").ConfigureAwait(false);
            await Reject(() => database.Resources.ReadByNameAsync(outsider, "team/" + suffix + "/conventions", token), "resource_not_found", "Another tenant must not read a resource by name.").ConfigureAwait(false);
            await Reject(() => database.Resources.UpdateAsync(outsider, conventions.Id, new ResourceRequest { Body = "x", Revision = 2 }, token: token), "resource_not_found", "Another tenant must not update a resource.").ConfigureAwait(false);
            await Reject(() => database.Resources.DeleteAsync(outsider, conventions.Id, 2, token), "resource_not_found", "Another tenant must not delete a resource.").ConfigureAwait(false);
            await Reject(() => database.Resources.RevisionsAsync(outsider, conventions.Id, token), "resource_not_found", "Another tenant must not read history.").ConfigureAwait(false);
            Require((await database.Resources.SearchAsync(outsider, "tabs", token: token).ConfigureAwait(false)).Count == 0 && (await database.Resources.SummariesAsync(outsider, token).ConfigureAwait(false)).Count == 0, "Another tenant must not list or search resources.");
            Resource theirs = await database.Resources.CreateAsync(outsider, new ResourceRequest { Name = "team/" + suffix + "/conventions", Body = "Theirs." }, token: token).ConfigureAwait(false);
            await database.Resources.AppendAsync(outsider, theirs.Id, "More.", token: token).ConfigureAwait(false);
            Require((await database.Resources.ReadByNameAsync(system, "team/" + suffix + "/conventions", token).ConfigureAwait(false)).Body == "Use tabs.", "Each tenant must have its own namespace.");
            await database.Identity.DeleteTenantAsync(system, other.Id, other.Name, token).ConfigureAwait(false);
            Require((await database.Resources.SummariesAsync(outsider, token).ConfigureAwait(false)).Count == 0, "Deleting a tenant must delete its resources.");
            await Reject(() => database.Resources.RevisionsAsync(outsider, theirs.Id, token), "resource_not_found", "Deleting a tenant must delete its resource history.").ConfigureAwait(false);

            // Deletion needs the current revision and removes history; changes are audited.
            await Reject(() => database.Resources.DeleteAsync(system, log.Id, 1, token), "revision_conflict", "Delete must check the current revision.").ConfigureAwait(false);
            await database.Resources.DeleteAsync(system, log.Id, log.Revision, token).ConfigureAwait(false);
            await Reject(() => database.Resources.ReadAsync(system, log.Id, token), "resource_not_found", "A deleted resource must be gone.").ConfigureAwait(false);
            foreach (string operation in new[] { "ResourceCreated", "ResourceUpdated", "ResourceRenamed", "ResourceLocked", "ResourceReverted", "ResourceImported", "ResourceDeleted" })
                Require((await database.Identity.AuditAsync(system, new AuditQuery { Operation = operation }, token).ConfigureAwait(false)).TotalCount > 0, operation + " must be audited.");
        }

        private static void FrontMatter()
        {
            Resource source = new Resource { Name = "a/b", Title = "Quote \" and colon: é", Description = "One line.", Tags = new List<string> { "x", "y" }, Locked = true, Body = "---\nnot front matter\n" };
            ResourceRequest parsed = ResourceMarkdown.Parse("ignored.md", ResourceMarkdown.Format(source));
            Require(parsed.Name == "a/b" && parsed.Title == source.Title && parsed.Description == "One line." && parsed.Tags!.SequenceEqual(source.Tags) && parsed.Locked == true && parsed.Body == source.Body, "Export must round-trip through import.");
            Require(ResourceMarkdown.Parse("Notes.MD", "plain").Name == "notes" && ResourceMarkdown.Parse("dir\\Read Me.markdown", "x").Name == "read-me", "Names must come from file names when the front matter has none.");
            Require(ResourceMarkdown.Parse("t.md", "---\ntags: [One, two]\nlocked: false\n---\nbody").Tags!.SequenceEqual(new[] { "One", "two" }), "Inline tag lists must parse.");
            try { ResourceMarkdown.Parse("bad.md", "---\nname: x\nno end"); throw new InvalidDataException("Unclosed front matter must be rejected."); }
            catch (DomainException failure) { Require(failure.Code == "invalid_front_matter", "Unclosed front matter must report invalid_front_matter."); }
        }

        private static RequestContext System() { return new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, IsTenantAdmin = true, Role = "admin" }; }

        private static async Task Reject(Func<Task> action, string code, string message)
        {
            try { await action().ConfigureAwait(false); }
            catch (DomainException failure)
            {
                Require(failure.Code == code, message + " Got " + failure.Code + ": " + failure.Message);
                return;
            }
            throw new InvalidDataException(message);
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
