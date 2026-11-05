namespace ArgueBot.Runners
{
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Runners;
    using ArgueBot.Core.Services;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Resolves credentials only for execution, isolates CLI login files, and scrubs emitted values.</summary>
    public sealed class CredentialRunner : IConversationToolRunner, IContextSizeEstimator
    {
        private readonly ConcurrentDictionary<string, EndpointCapacityGate> _EndpointGates = new ConcurrentDictionary<string, EndpointCapacityGate>(StringComparer.Ordinal);
        private readonly RunnerRouter _Runner;
        private readonly CredentialVault _Vault;
        private readonly DatabaseDriverBase _Database;
        private readonly RequestContext _Context;
        private readonly string _Directory;
        private readonly IReadOnlyList<string> _McpCommand;
        /// <summary>Create a service-owned runner with protected transient credential storage.</summary>
        public CredentialRunner(bool allowCli, CredentialVault vault, DatabaseDriverBase database, RequestContext context, string directory, IReadOnlyList<string>? mcpCommand = null)
        { _Runner = new RunnerRouter(allowCli); _Vault = vault; _Database = database; _Context = context; _Directory = directory; _McpCommand = mcpCommand ?? new List<string>(); Directory.CreateDirectory(directory); HomePermissions.Directory(directory); CleanupAbandonedHomes(directory); }
        private static void CleanupAbandonedHomes(string directory)
        {
            // The service has already acquired exclusive database ownership. Never traverse unrelated or linked homes.
            foreach (string candidate in Directory.EnumerateDirectories(directory))
            {
                if (!Guid.TryParseExact(Path.GetFileName(candidate), "N", out Guid _) || (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0) continue;
                string marker = Path.Combine(candidate, ".arguebot-credential-home");
                if (!File.Exists(marker) || (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0) continue;
                Directory.Delete(candidate, true);
            }
        }
        /// <inheritdoc />
        public long MeasureContext(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, IReadOnlyList<ConversationToolDefinition> tools, bool allowLocalTools)
        { return _Runner.MeasureContext(profile, instructions, history, tools, allowLocalTools); }
        /// <inheritdoc />
        public Task GenerateAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, CancellationToken token = default)
        { return GenerateWithToolsAsync(profile, instructions, history, onText, (_, _) => Task.CompletedTask, token); }
        /// <inheritdoc />
        public Task GenerateWithToolsAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, Func<ToolActivity, CancellationToken, Task> onTool, CancellationToken token = default)
        { return ExecuteAsync(profile, (prepared, text, tool) => _Runner.GenerateWithToolsAsync(prepared, instructions, history, text, tool, token), onText, onTool, token); }
        /// <inheritdoc />
        public Task GenerateWithContextAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, IReadOnlyList<ConversationToolDefinition> tools, Func<ConversationToolCall, CancellationToken, Task<string>> execute, bool allowLocalTools, Func<string, CancellationToken, Task> onText, Func<ToolActivity, CancellationToken, Task> onTool, CancellationToken token = default)
        { return ExecuteAsync(profile, (prepared, text, tool) => _Runner.GenerateWithContextAsync(prepared, instructions, history, tools, execute, allowLocalTools, text, tool, token), onText, onTool, token); }
        private async Task ExecuteAsync(RunnerProfile profile, Func<RunnerProfile, Func<string, CancellationToken, Task>, Func<ToolActivity, CancellationToken, Task>, Task> invoke, Func<string, CancellationToken, Task> onText, Func<ToolActivity, CancellationToken, Task> onTool, CancellationToken token)
        {
            RunnerProfile prepared = JsonSerializer.Deserialize<RunnerProfile>(JsonSerializer.Serialize(profile))!;
            prepared.RuntimeMcpCommand.AddRange(_McpCommand);
            // Resolve endpoints and secrets inside the invoking run's tenant, never the service's own context.
            RequestContext scope = new RequestContext { TenantId = profile.RuntimeTenantId.Length > 0 ? profile.RuntimeTenantId : _Context.TenantId, UserId = _Context.UserId, IsAuthenticated = true, IsAdmin = _Context.IsAdmin, IsTenantAdmin = true, Role = _Context.Role, PrincipalType = "Service", CorrelationId = _Context.CorrelationId };
            List<string> secrets = new List<string>(); string? home = null; IDisposable? capacityLease = null;
            try
            {
                if (prepared.RestrictEnvironment)
                {
                    foreach (string name in prepared.EnvironmentVariables)
                    {
                        string? value = Environment.GetEnvironmentVariable(name);
                        if (value == null) continue;
                        prepared.RuntimeEnvironment[name] = value;
                        // Redact credential-like values only; redacting ordinary configuration such as a region or "1" would corrupt output and hide failure diagnostics.
                        if (Processes.HarnessEnvironment.IsSecretName(name) && value.Length >= 4) secrets.Add(value);
                    }
                }
                if (prepared.Kind == "rest")
                {
                    int capacity = 1; long capacityRevision = 0;
                    string identity = prepared.TransportId.Length > 0 ? prepared.TransportId : prepared.Endpoint;
                    if (prepared.TransportId.Length > 0)
                    {
                        EndpointProfile? endpoint = (await _Database.Catalog.EndpointsAsync(scope, token, prepared.TransportId).ConfigureAwait(false)).FirstOrDefault();
                        if (endpoint == null || !endpoint.Enabled) throw new DomainException(409, "configuration_unavailable", "The endpoint is unavailable or disabled. Enable it before retrying.");
                        capacity = endpoint.MaxConcurrentInvocations; capacityRevision = endpoint.Revision;
                    }
                    long waiting = Stopwatch.GetTimestamp();
                    try { capacityLease = await _EndpointGates.GetOrAdd(identity, _ => new EndpointCapacityGate()).EnterAsync(capacity, capacityRevision, token).ConfigureAwait(false); }
                    finally { if (profile.RuntimeEndpointWait != null) await profile.RuntimeEndpointWait((long)Stopwatch.GetElapsedTime(waiting).TotalMilliseconds, CancellationToken.None).ConfigureAwait(false); }
                }
                foreach (HttpAuthBinding binding in prepared.AuthBindings)
                {
                    CredentialMetadata? used = null;
                    string value;
                    if (binding.CredentialId.Length > 0)
                    {
                        if (!prepared.RuntimeCredentials.TryGetValue(binding.CredentialId, out string? cached)) value = await _Vault.ResolveAsync(scope, binding.CredentialId, token, metadata => used = metadata).ConfigureAwait(false);
                        else value = cached;
                        if (used != null && profile.RuntimeCredentialResolved != null) await profile.RuntimeCredentialResolved(used, token).ConfigureAwait(false);
                    }
                    else value = binding.EnvironmentVariable.Length > 0 ? Environment.GetEnvironmentVariable(binding.EnvironmentVariable) ?? "" : binding.Value;
                    if (binding.CredentialId.Length > 0) prepared.RuntimeCredentials[binding.CredentialId] = value;
                    if (binding.CredentialId.Length > 0 || binding.EnvironmentVariable.Length > 0) secrets.Add(value);
                }
                if (prepared.SecretEnvironmentVariable.Length > 0) secrets.Add(Environment.GetEnvironmentVariable(prepared.SecretEnvironmentVariable) ?? "");
                if (prepared.CredentialId.Length > 0)
                {
                    CredentialMetadata metadata = (await _Database.Credentials.EnumerateAsync(scope, token, prepared.CredentialId).ConfigureAwait(false)).FirstOrDefault(item => item.Id == prepared.CredentialId) ?? throw new DomainException(409, "credential_unavailable", "The configured CLI credential is unavailable.");
                    string value = await _Vault.ResolveAsync(scope, metadata.Id, token, current => metadata = current).ConfigureAwait(false); secrets.Add(value);
                    if (prepared.Provider == "claude" && metadata.Kind == "claude_oauth") prepared.RuntimeEnvironment["CLAUDE_CODE_OAUTH_TOKEN"] = value;
                    else if (prepared.Provider == "codex" && metadata.Kind == "codex_auth_json")
                    {
                        value = await _Vault.PrepareCodexAsync(scope, metadata.Id, token, current => metadata = current).ConfigureAwait(false);
                        using (JsonDocument document = JsonDocument.Parse(value)) AddSecrets(document.RootElement, secrets);
                        home = Path.Combine(_Directory, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home); HomePermissions.Directory(home);
                        string marker = Path.Combine(home, ".arguebot-credential-home"); await File.WriteAllTextAsync(marker, "arguebot invocation credential home", token).ConfigureAwait(false); HomePermissions.File(marker);
                        string path = Path.Combine(home, "auth.json");
                        await File.WriteAllTextAsync(path, CredentialVault.CodexRunCopy(value), token).ConfigureAwait(false); HomePermissions.File(path);
                        prepared.RuntimeEnvironment["CODEX_HOME"] = home;
                    }
                    else throw new DomainException(409, "credential_kind_mismatch", "Choose a Claude OAuth token for Claude Code or a Codex auth.json for Codex.");
                    if (profile.RuntimeCredentialResolved != null) await profile.RuntimeCredentialResolved(metadata, token).ConfigureAwait(false);
                }
                SecretTextFilter filter = new SecretTextFilter(secrets, onText);
                try
                {
                    await invoke(prepared, filter.WriteAsync, async (activity, cancellation) => { activity.ArgumentsJson = filter.Scrub(activity.ArgumentsJson); activity.Result = filter.Scrub(activity.Result); await onTool(activity, cancellation).ConfigureAwait(false); }).ConfigureAwait(false);
                    await filter.CompleteAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (secrets.Count > 0)
                {
                    // Cancellation diagnostics can contain provider output; preserve cancellation without attaching it.
                    throw new OperationCanceledException("The credentialed invocation was cancelled.", token);
                }
                catch (Exception failure) when (secrets.Count > 0 && failure is not OperationCanceledException && failure is not OutOfMemoryException)
                {
                    // Provider and process diagnostics may echo headers or login files. Do not attach the original exception.
                    if (failure is DomainException domain) throw new DomainException(domain.StatusCode, domain.Code, filter.Scrub(domain.Message));
                    throw new DomainException(502, "credentialed_generation_failed", "The credentialed invocation failed. Check the endpoint, login status and runner configuration before explicitly retrying.");
                }
            }
            finally
            {
                capacityLease?.Dispose();
                prepared.RuntimeCredentials.Clear(); prepared.RuntimeEnvironment.Clear(); secrets.Clear();
                if (home != null && Directory.Exists(home)) Directory.Delete(home, true);
            }
        }
        private static void AddSecrets(JsonElement element, List<string> secrets)
        {
            if (element.ValueKind == JsonValueKind.Object) foreach (JsonProperty property in element.EnumerateObject()) AddSecrets(property.Value, secrets);
            else if (element.ValueKind == JsonValueKind.String && element.GetString()!.Length > 0) secrets.Add(element.GetString()!);
        }
    }
}
