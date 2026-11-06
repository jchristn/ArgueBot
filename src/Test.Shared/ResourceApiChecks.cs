namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Net.Http.Json;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;

    /// <summary>Resource REST routes against a running service: authentication, every route, names with slashes, revision conflicts, export and import, and tenant isolation.</summary>
    internal static class ResourceApiChecks
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        internal static async Task RunAsync(string endpoint, string home, CancellationToken token)
        {
            using (HttpClient http = new HttpClient(LocalServiceConnector.CreateHttpHandler(home), true) { BaseAddress = new Uri(endpoint.TrimEnd('/') + "/") })
            using (ArgueBotClient admin = LocalServiceConnector.CreateClient(endpoint, home, IdentitySeed.Token))
            using (ArgueBotClient outsider = LocalServiceConnector.CreateClient(endpoint, home, ""))
            {
                // Every resource route needs a token.
                foreach (string path in new[] { "v2.0/api/resources", "v2.0/api/resources/res_missing", "v2.0/api/resources/by-name/billing", "v2.0/api/resources/res_missing/revisions", "v2.0/api/resources/res_missing/export" })
                    await Status(http, HttpMethod.Get, path, "", 401, token).ConfigureAwait(false);

                // Create, read by ID and by a name with slashes, including names that look like sub-routes.
                Resource billing = await admin.CreateResourceAsync(new ResourceRequest { Name = "Finance/Billing", Title = "Billing", Description = "Invoices and refunds.", Tags = new List<string> { "finance" }, Body = "# Billing\nInvoices go out on the 1st." }, token).ConfigureAwait(false);
                Require(billing.Name == "finance/billing" && billing.Revision == 1, "POST must create a lowercase-named resource.");
                Require((await admin.ResourceAsync(billing.Id, token).ConfigureAwait(false)).Body == billing.Body, "GET by ID must return the body.");
                Require((await admin.ResourceAsync("FINANCE/billing", token).ConfigureAwait(false)).Id == billing.Id, "GET by-name must accept slashes and ignore case.");
                Require((await admin.SendAsync<Resource>(HttpMethod.Get, "v2.0/api/resources/by-name/finance%2Fbilling", token: token).ConfigureAwait(false)).Id == billing.Id, "GET by-name must accept an encoded slash.");
                foreach (string tricky in new[] { "revisions", "export", "a/revisions", "revisions/3", "x/export" })
                {
                    Resource created = await admin.CreateResourceAsync(new ResourceRequest { Name = tricky, Body = tricky }, token).ConfigureAwait(false);
                    Require((await admin.ResourceAsync(tricky, token).ConfigureAwait(false)).Id == created.Id, "GET by-name must resolve " + tricky + " rather than a sub-route.");
                }
                await Expect(() => admin.ResourceAsync("finance/missing", token), 404, "resource_not_found").ConfigureAwait(false);
                await Expect(() => admin.CreateResourceAsync(new ResourceRequest { Name = "finance/BILLING" }, token), 409, "resource_exists").ConfigureAwait(false);
                await Expect(() => admin.CreateResourceAsync(new ResourceRequest { Name = "bad name" }, token), 400, "invalid_resource_name").ConfigureAwait(false);
                await Expect(() => admin.CreateResourceAsync(new ResourceRequest { Name = "huge", Body = new string('x', ResourceRules.MaxBodyBytes + 1) }, token), 413, "resource_too_large").ConfigureAwait(false);

                // List with filters and paging.
                ResourcePage tagged = await admin.ResourcesAsync(tag: "finance", token: token).ConfigureAwait(false);
                Require(tagged.TotalCount == 1 && tagged.Items[0].Id == billing.Id && !JsonSerializer.Serialize(tagged).Contains("Invoices go out", StringComparison.Ordinal), "GET list must filter by tag and omit bodies.");
                ResourcePage page = await admin.ResourcesAsync(pageNumber: 2, pageSize: 2, token: token).ConfigureAwait(false);
                Require(page.Items.Count == 2 && page.TotalCount == 6, "GET list must page.");
                await Status(http, HttpMethod.Get, "v2.0/api/resources?pageSize=500", IdentitySeed.Token, 400, token).ConfigureAwait(false);

                // Update with revision; stale revision conflicts; rename and lock through PUT.
                await Expect(() => admin.UpdateResourceAsync(billing.Id, new ResourceRequest { Body = "stale", Revision = 9 }, token), 409, "revision_conflict").ConfigureAwait(false);
                Resource updated = await admin.UpdateResourceAsync(billing.Id, new ResourceRequest { Body = "# Billing\nInvoices go out on the 2nd.", Revision = 1 }, token).ConfigureAwait(false);
                Require(updated.Revision == 2 && updated.Title == "Billing", "PUT must keep omitted fields.");
                Resource locked = await admin.UpdateResourceAsync(billing.Id, new ResourceRequest { Name = "finance/invoicing", Locked = true, Revision = 2 }, token).ConfigureAwait(false);
                Require(locked.Name == "finance/invoicing" && locked.Locked && locked.Revision == 3, "PUT must rename and lock.");

                // History, one revision, revert.
                List<ResourceRevision> history = await admin.ResourceRevisionsAsync(billing.Id, token).ConfigureAwait(false);
                Require(history.Select(item => item.Revision).SequenceEqual(new long[] { 3, 2, 1 }) && history.All(item => item.Body.Length == 0 && item.AuthorKind == "user"), "GET revisions must list newest first without bodies.");
                ResourceRevision first = await admin.ResourceRevisionAsync(billing.Id, 1, token).ConfigureAwait(false);
                Require(first.Body.Contains("1st", StringComparison.Ordinal) && first.Name == "finance/billing", "GET one revision must return its body.");
                await Expect(() => admin.ResourceRevisionAsync(billing.Id, 42, token), 404, "revision_not_found").ConfigureAwait(false);
                await Expect(() => admin.RevertResourceAsync(billing.Id, new ResourceRevertRequest { TargetRevision = 1, Revision = 2 }, token), 409, "revision_conflict").ConfigureAwait(false);
                Resource reverted = await admin.RevertResourceAsync(billing.Id, new ResourceRevertRequest { TargetRevision = 1, Revision = 3 }, token).ConfigureAwait(false);
                Require(reverted.Revision == 4 && reverted.Body.Contains("1st", StringComparison.Ordinal) && reverted.Name == "finance/invoicing" && reverted.Locked, "POST revert must restore the body as a new revision.");

                // Export as markdown, then import it back under a new name.
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "v2.0/api/resources/" + billing.Id + "/export"))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", IdentitySeed.Token);
                    using (HttpResponseMessage response = await http.SendAsync(request, token).ConfigureAwait(false))
                    {
                        Require(response.StatusCode == HttpStatusCode.OK && response.Content.Headers.ContentType?.MediaType == "text/markdown" && (response.Content.Headers.ContentDisposition?.FileName ?? "").Contains("finance_invoicing.md", StringComparison.Ordinal), "GET export must return a markdown attachment.");
                    }
                }
                string exported = await admin.ExportResourceAsync(billing.Id, token).ConfigureAwait(false);
                Require(exported.StartsWith("---\nname: finance/invoicing\n", StringComparison.Ordinal) && exported.Contains("locked: true", StringComparison.Ordinal) && exported.EndsWith("Invoices go out on the 1st.", StringComparison.Ordinal), "Export must include front matter and the body.");
                ResourceImportResult imported = await admin.ImportResourcesAsync(new ResourceImportRequest { Files = new List<ResourceImportFile> { new ResourceImportFile { FileName = "copy.md", Content = exported.Replace("name: finance/invoicing", "name: finance/copy", StringComparison.Ordinal) } } }, token).ConfigureAwait(false);
                Require(imported.Created == 1 && imported.Items[0].Name == "finance/copy" && imported.Items[0].Locked, "POST import must create resources from front matter.");
                await Expect(() => admin.ImportResourcesAsync(new ResourceImportRequest { Files = new List<ResourceImportFile> { new ResourceImportFile { FileName = "copy.md", Content = "---\nname: finance/copy\n---\nagain" } } }, token), 409, "resource_exists").ConfigureAwait(false);
                await Expect(() => admin.ImportResourcesAsync(new ResourceImportRequest(), token), 400, "invalid_import").ConfigureAwait(false);

                // Another tenant sees none of it and cannot change it.
                Tenant tenant = await admin.SendAsync<Tenant>(HttpMethod.Post, "v2.0/api/tenants", new TenantRequest { Name = "Resource fixture tenant" }, token).ConfigureAwait(false);
                // Sign-in is rate limited per address by earlier checks, so the outsider uses a bearer-token credential.
                admin.TenantId = tenant.Id;
                User outsiderUser = await admin.SendAsync<User>(HttpMethod.Post, "v2.0/api/users", new UserRequest { FirstName = "Resource", LastName = "Outsider", Email = "resources@example.test", Password = "outsider-password" }, token).ConfigureAwait(false);
                AuthCredentialCreated credential = await admin.SendAsync<AuthCredentialCreated>(HttpMethod.Post, "v2.0/api/users/" + outsiderUser.Id + "/credentials", new AuthCredentialRequest { Name = "Resource fixture" }, token).ConfigureAwait(false);
                admin.TenantId = "";
                outsider.Token = credential.Token;
                Require((await outsider.ResourcesAsync(token: token).ConfigureAwait(false)).TotalCount == 0, "Another tenant must list no resources.");
                await Expect(() => outsider.ResourceAsync(billing.Id, token), 404, "resource_not_found").ConfigureAwait(false);
                await Expect(() => outsider.ResourceAsync("finance/invoicing", token), 404, "resource_not_found").ConfigureAwait(false);
                await Expect(() => outsider.UpdateResourceAsync(billing.Id, new ResourceRequest { Body = "x", Revision = 4 }, token), 404, "resource_not_found").ConfigureAwait(false);
                await Expect(() => outsider.ResourceRevisionsAsync(billing.Id, token), 404, "resource_not_found").ConfigureAwait(false);
                await Expect(() => outsider.ExportResourceAsync(billing.Id, token), 404, "resource_not_found").ConfigureAwait(false);
                await Expect(() => outsider.RevertResourceAsync(billing.Id, new ResourceRevertRequest { TargetRevision = 1, Revision = 4 }, token), 404, "resource_not_found").ConfigureAwait(false);
                await Expect(() => outsider.DeleteResourceAsync(billing.Id, 4, token), 404, "resource_not_found").ConfigureAwait(false);

                // Delete with revision.
                await Expect(() => admin.DeleteResourceAsync(billing.Id, 1, token), 409, "revision_conflict").ConfigureAwait(false);
                await admin.DeleteResourceAsync(billing.Id, 4, token).ConfigureAwait(false);
                await Expect(() => admin.ResourceAsync(billing.Id, token), 404, "resource_not_found").ConfigureAwait(false);
                await Expect(() => admin.ResourceRevisionsAsync(billing.Id, token), 404, "resource_not_found").ConfigureAwait(false);
            }
        }

        private static async Task Status(HttpClient http, HttpMethod method, string path, string bearer, int status, CancellationToken token)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(method, path))
            {
                if (bearer.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                using (HttpResponseMessage response = await http.SendAsync(request, token).ConfigureAwait(false))
                    if ((int)response.StatusCode != status) throw new InvalidDataException(method + " " + path + " returned " + (int)response.StatusCode + " instead of " + status + ".");
            }
        }

        private static async Task Expect(Func<Task> action, int status, string code)
        {
            try { await action().ConfigureAwait(false); }
            catch (DomainException ex) when (ex.StatusCode == status && ex.Code == code) { return; }
            catch (DomainException ex) { throw new InvalidDataException("Expected " + status + " " + code + " but got " + ex.StatusCode + " " + ex.Code + ": " + ex.Message); }
            throw new InvalidDataException("Expected " + status + " " + code + " but the request succeeded.");
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
