namespace ArgueBot.Core.Services
{
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Service-owned AES-GCM credential encryption. No public route exposes resolved values.</summary>
    public sealed class CredentialVault : IDisposable
    {
        private readonly DatabaseDriverBase _Database;
        private CredentialKeyRing _KeyRing;
        private readonly string _KeyRingPath;
        private readonly SemaphoreSlim _KeyGate = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _RefreshGates = new ConcurrentDictionary<string, SemaphoreSlim>();
        private readonly HttpClient _RefreshHttp;
        private CredentialVault(DatabaseDriverBase database, CredentialKeyRing keyRing, string keyRingPath, HttpMessageHandler? refreshHandler) { _Database = database; _KeyRing = keyRing; _KeyRingPath = keyRingPath; _RefreshHttp = new HttpClient(refreshHandler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 131072 }; }
        /// <summary>Open a protected service key; never silently replace a lost key for existing credentials.</summary>
        public static async Task<CredentialVault> OpenAsync(DatabaseDriverBase database, string directory, RequestContext context, CancellationToken token = default, HttpMessageHandler? refreshHandler = null)
        {
            Directory.CreateDirectory(directory); HomePermissions.Directory(directory);
            string keyRingPath = Path.Combine(directory, "credential.keys.json");
            if (File.Exists(keyRingPath))
            {
                HomePermissions.File(keyRingPath);
                CredentialKeyRing ring;
                try { ring = JsonSerializer.Deserialize<CredentialKeyRing>(await File.ReadAllTextAsync(keyRingPath, token).ConfigureAwait(false)) ?? throw new InvalidDataException(); }
                catch (JsonException) { throw new InvalidDataException("The protected credential keyring is invalid. Restore it from the matching key backup."); }
                if (ring.Version != 1 || ring.Keys == null || ring.ActiveKeyId == null || ring.Keys.Count == 0 || !ring.Keys.ContainsKey(ring.ActiveKeyId) || ring.Keys.Any(pair => pair.Value == null || pair.Value.Length != 32 || pair.Key.Length == 0 || pair.Key.Contains(':'))) throw new InvalidDataException("The protected credential keyring is invalid. Restore it from the matching key backup.");
                return new CredentialVault(database, ring, keyRingPath, refreshHandler);
            }
            string path = Path.Combine(directory, "credential.key");
            if (!File.Exists(path))
            {
                if ((await database.Credentials.EnumerateAsync(context, token).ConfigureAwait(false)).Count > 0) throw new InvalidDataException("The credential encryption key is missing. Restore the original protected key before starting the service.");
                byte[] generated = RandomNumberGenerator.GetBytes(32);
                using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { HomePermissions.File(path); await stream.WriteAsync(generated, token).ConfigureAwait(false); await stream.FlushAsync(token).ConfigureAwait(false); }
                CryptographicOperations.ZeroMemory(generated);
            }
            HomePermissions.File(path);
            byte[] key = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
            if (key.Length != 32) throw new InvalidDataException("The credential encryption key is invalid. Restore the original protected key.");
            if (await database.Credentials.CountOtherKeysAsync(context, "legacy", token).ConfigureAwait(false) > 0) { CryptographicOperations.ZeroMemory(key); throw new InvalidDataException("Stored credentials use a rotated key, but the protected keyring credential.keys.json is missing. Restore it from the matching key backup before starting the service."); }
            return new CredentialVault(database, new CredentialKeyRing { Keys = new Dictionary<string, byte[]> { ["legacy"] = key } }, keyRingPath, refreshHandler);
        }
        /// <summary>Import or revision-check a replacement. Validation errors never include submitted values.</summary>
        public async Task<CredentialMetadata> SaveAsync(RequestContext context, string id, CredentialRequest request, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200 || request.Name.Any(char.IsControl)) throw new DomainException(400, "invalid_credential", "Use a safe display name up to 200 characters.");
            if (string.IsNullOrWhiteSpace(request.SecretValue) || request.SecretValue.Length > 65536) throw Invalid();
            if (request.Kind == "codex_auth_json") ValidateCodex(request.SecretValue);
            else if (!new[] { "token", "claude_oauth", "http_bearer", "http_header", "http_query" }.Contains(request.Kind) || request.SecretValue.Any(char.IsControl)) throw Invalid();
            if (request.Kind == "http_header" || request.Kind == "http_query") CatalogValidation.ParameterName(request.Kind == "http_header" ? "header" : "query", request.ParameterName);
            else if (request.ParameterName.Length > 0) throw new DomainException(400, "invalid_credential", "Only header and query credentials take a parameter name.", "parameterName");
            await _KeyGate.WaitAsync(token).ConfigureAwait(false);
            try { return await _Database.Credentials.SaveAsync(context, id, request, Encrypt, token).ConfigureAwait(false); }
            finally { _KeyGate.Release(); }
        }
        /// <summary>Decrypt a ready credential for the moderator's explicit reveal request. Never call this for model or tool output.</summary>
        public async Task<RevealedCredential> RevealAsync(RequestContext context, string id, CancellationToken token = default)
        {
            CredentialMetadata? metadata = null;
            string value = await ResolveAsync(context, id, token, current => metadata = current).ConfigureAwait(false);
            if (metadata == null) throw new DomainException(404, "credential_not_found", "No ready credential has this ID. Disabled credentials cannot be revealed.");
            return new RevealedCredential { Id = metadata.Id, Name = metadata.Name, Kind = metadata.Kind, ParameterName = metadata.ParameterName, Revision = metadata.Revision, SecretValue = value };
        }
        /// <summary>Resolve only within the execution service. Callers must never serialize or log the result.</summary>
        public async Task<string> ResolveAsync(RequestContext context, string id, CancellationToken token = default, Action<CredentialMetadata>? resolved = null)
        {
            await _KeyGate.WaitAsync(token).ConfigureAwait(false);
            try { return Decrypt(id, await _Database.Credentials.ResolveAsync(context, id, token, resolved).ConfigureAwait(false)); }
            finally { _KeyGate.Release(); }
        }
        private string Decrypt(string id, string ciphertext)
        {
            try
            {
                string keyId = "legacy";
                if (ciphertext.StartsWith("v1:", StringComparison.Ordinal))
                {
                    int separator = ciphertext.IndexOf(':', 3);
                    if (separator < 0) throw new FormatException();
                    keyId = ciphertext.Substring(3, separator - 3); ciphertext = ciphertext.Substring(separator + 1);
                }
                if (!_KeyRing.Keys.TryGetValue(keyId, out byte[]? key)) throw new CryptographicException();
                byte[] envelope = Convert.FromBase64String(ciphertext); byte[] plaintext = new byte[envelope.Length - 28];
                try
                {
                    using (AesGcm cipher = new AesGcm(key, 16)) cipher.Decrypt(envelope.AsSpan(0, 12), envelope.AsSpan(28), envelope.AsSpan(12, 16), plaintext, Encoding.UTF8.GetBytes(id));
                    return Encoding.UTF8.GetString(plaintext);
                }
                finally { CryptographicOperations.ZeroMemory(plaintext); }
            }
            catch (Exception failure) when (failure is CryptographicException || failure is FormatException || failure is ArgumentException || failure is OverflowException)
            { throw new DomainException(409, "credential_unreadable", "The stored credential cannot be decrypted. Restore the original keyring or replace the credential."); }
        }
        /// <summary>Read safe key identities; never exposes key material.</summary>
        public async Task<CredentialKeyStatus> KeyStatusAsync(CancellationToken token = default)
        {
            await _KeyGate.WaitAsync(token).ConfigureAwait(false);
            try { return new CredentialKeyStatus { ActiveKeyId = _KeyRing.ActiveKeyId, RetainedKeyCount = _KeyRing.Keys.Count }; }
            finally { _KeyGate.Release(); }
        }
        /// <summary>Durably install a new master key before transactional re-encryption; old keys remain for crash recovery and backups until explicitly retired.</summary>
        /// <remarks>Every stored credential is decrypted first. An unreadable enabled credential refuses rotation before any key changes; an unreadable disabled credential is left unchanged and counted as skipped.</remarks>
        public async Task<CredentialKeyStatus> RotateKeyAsync(RequestContext context, RotateCredentialKeyRequest request, CancellationToken token = default)
        {
            DatabaseDriverBase.RequireWrite(context);
            await _KeyGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (request.ExpectedKeyId != _KeyRing.ActiveKeyId) throw new DomainException(409, "key_changed", "The active key changed. Refresh key status before rotating again.");
                List<string> unreadable = new List<string>(); HashSet<string> skipped = new HashSet<string>(StringComparer.Ordinal);
                await _Database.Credentials.InspectAsync((id, name, status, ciphertext) =>
                {
                    try { Decrypt(id, ciphertext); }
                    catch (DomainException) { if (status == "disabled") skipped.Add(id); else unreadable.Add(name); }
                }, token).ConfigureAwait(false);
                if (unreadable.Count > 0) throw new DomainException(409, "credential_unreadable", unreadable.Count + (unreadable.Count == 1 ? " credential cannot" : " credentials cannot") + " be decrypted with the retained keys: " + string.Join(", ", unreadable.Take(10)) + (unreadable.Count > 10 ? " and others" : "") + ". Replace each with a fresh value or disable it, then rotate again. No key was changed.");
                CredentialKeyRing next = new CredentialKeyRing { ActiveKeyId = Guid.NewGuid().ToString("N"), Keys = new Dictionary<string, byte[]>(_KeyRing.Keys, StringComparer.Ordinal) };
                next.Keys.Add(next.ActiveKeyId, RandomNumberGenerator.GetBytes(32));
                try { await WriteRingAsync(next, token).ConfigureAwait(false); }
                catch { CryptographicOperations.ZeroMemory(next.Keys[next.ActiveKeyId]); throw; }
                _KeyRing = next;
                long count = await _Database.Credentials.ReencryptAsync(context, (id, value) => skipped.Contains(id) ? value : Encrypt(id, Decrypt(id, value)), token).ConfigureAwait(false);
                return new CredentialKeyStatus { ActiveKeyId = _KeyRing.ActiveKeyId, RetainedKeyCount = _KeyRing.Keys.Count, ReencryptedCredentials = count - skipped.Count, SkippedCredentials = skipped.Count };
            }
            finally { _KeyGate.Release(); }
        }
        /// <summary>Remove every earlier key once no stored credential uses one. Backups created before the latest rotation can no longer decrypt their credentials with this keyring.</summary>
        public async Task<CredentialKeyStatus> RetireKeysAsync(RequestContext context, RotateCredentialKeyRequest request, CancellationToken token = default)
        {
            DatabaseDriverBase.RequireWrite(context);
            await _KeyGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (request.ExpectedKeyId != _KeyRing.ActiveKeyId) throw new DomainException(409, "key_changed", "The active key changed. Refresh key status before retiring keys.");
                int retired = _KeyRing.Keys.Count - 1;
                if (retired == 0) return new CredentialKeyStatus { ActiveKeyId = _KeyRing.ActiveKeyId, RetainedKeyCount = 1 };
                long pending = await _Database.Credentials.CountNotUsingKeyAsync(_KeyRing.ActiveKeyId, token).ConfigureAwait(false);
                if (pending > 0) throw new DomainException(409, "keys_in_use", pending + (pending == 1 ? " credential is" : " credentials are") + " still encrypted with an earlier key. Rotate again to re-encrypt them, replacing any that cannot be read, before retiring earlier keys.");
                CredentialKeyRing next = new CredentialKeyRing { ActiveKeyId = _KeyRing.ActiveKeyId, Keys = new Dictionary<string, byte[]>(StringComparer.Ordinal) { [_KeyRing.ActiveKeyId] = _KeyRing.Keys[_KeyRing.ActiveKeyId] } };
                await WriteRingAsync(next, token).ConfigureAwait(false);
                foreach (KeyValuePair<string, byte[]> key in _KeyRing.Keys) if (key.Key != next.ActiveKeyId) CryptographicOperations.ZeroMemory(key.Value);
                _KeyRing = next;
                return new CredentialKeyStatus { ActiveKeyId = _KeyRing.ActiveKeyId, RetainedKeyCount = 1, RetiredKeyCount = retired };
            }
            finally { _KeyGate.Release(); }
        }
        private async Task WriteRingAsync(CredentialKeyRing next, CancellationToken token)
        {
            string temporary = _KeyRingPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    HomePermissions.File(temporary);
                    await JsonSerializer.SerializeAsync(stream, next, cancellationToken: token).ConfigureAwait(false);
                    await stream.FlushAsync(token).ConfigureAwait(false); stream.Flush(true);
                }
                File.Move(temporary, _KeyRingPath, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        /// <summary>Serialize Codex refresh for this service and persist an uncertainty marker before consuming refresh authority. The marker is released when the request provably did not reach the login service or was refused with 429 or 5xx.</summary>
        public async Task<string> PrepareCodexAsync(RequestContext context, string id, CancellationToken token = default, Action<CredentialMetadata>? resolved = null)
        {
            SemaphoreSlim gate = _RefreshGates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1)); await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                CredentialMetadata metadata = (await _Database.Credentials.EnumerateAsync(context, token, id).ConfigureAwait(false)).FirstOrDefault(item => item.Id == id && item.Kind == "codex_auth_json") ?? throw Reauthenticate();
                string value = await ResolveAsync(context, id, token, current => metadata = current).ConfigureAwait(false);
                JsonObject document = ValidateCodex(value);
                if (!string.IsNullOrWhiteSpace(document["OPENAI_API_KEY"]?.GetValue<string>())) { resolved?.Invoke(metadata); return value; }
                JsonObject tokens = document["tokens"] as JsonObject ?? throw Reauthenticate();
                DateTimeOffset? expires = TokenExpiry(tokens["access_token"]?.GetValue<string>() ?? "");
                if (expires > DateTimeOffset.UtcNow.AddMinutes(5)) { resolved?.Invoke(metadata); return value; }
                if (!expires.HasValue && DateTimeOffset.TryParse(document["last_refresh"]?.GetValue<string>(), out DateTimeOffset refreshed) && refreshed > DateTimeOffset.UtcNow.AddDays(-7)) { resolved?.Invoke(metadata); return value; }
                string refresh = tokens["refresh_token"]?.GetValue<string>() ?? "";
                if (refresh.Length == 0) throw Reauthenticate();
                token.ThrowIfCancellationRequested();
                long revision = await _Database.Credentials.ClaimRefreshAsync(context, id, metadata.Revision, token).ConfigureAwait(false);
                try
                {
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://auth.openai.com/oauth/token"))
                    {
                        request.Content = new StringContent(JsonSerializer.Serialize(new { grant_type = "refresh_token", client_id = "app_EMoamEEZ73f0CkXaXp7hrann", refresh_token = refresh }), Encoding.UTF8, "application/json");
                        HttpResponseMessage response;
                        try { response = await _RefreshHttp.SendAsync(request, token).ConfigureAwait(false); }
                        catch (HttpRequestException failure) when (failure.HttpRequestError == HttpRequestError.NameResolutionError || failure.HttpRequestError == HttpRequestError.ConnectionError || failure.HttpRequestError == HttpRequestError.ProxyTunnelError || failure.HttpRequestError == HttpRequestError.SecureConnectionError)
                        {
                            // The request never reached the login service, so its refresh token was not consumed.
                            await _Database.Credentials.ReleaseRefreshAsync(context, id, revision, CancellationToken.None).ConfigureAwait(false);
                            throw RefreshUnavailable();
                        }
                        using (response)
                        {
                            if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                            {
                                // A rate-limited or failed login service did not issue tokens; the stored login stays valid for a later retry.
                                await _Database.Credentials.ReleaseRefreshAsync(context, id, revision, CancellationToken.None).ConfigureAwait(false);
                                throw RefreshUnavailable();
                            }
                            if (!response.IsSuccessStatusCode) throw Reauthenticate();
                            JsonObject updated = JsonNode.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)) as JsonObject ?? throw Reauthenticate();
                            string access = updated["access_token"]?.GetValue<string>() ?? "";
                            if (access.Length == 0) throw Reauthenticate();
                            tokens["access_token"] = access;
                            if (updated["refresh_token"] != null) tokens["refresh_token"] = updated["refresh_token"]!.GetValue<string>();
                            if (updated["id_token"] != null) tokens["id_token"] = updated["id_token"]!.GetValue<string>();
                            document["last_refresh"] = DateTimeOffset.UtcNow.ToString("O"); value = document.ToJsonString();
                            metadata = await SaveAsync(context, id, new CredentialRequest { Name = metadata.Name, Kind = metadata.Kind, Revision = revision, SecretValue = value }, CancellationToken.None).ConfigureAwait(false);
                            resolved?.Invoke(metadata); return value;
                        }
                    }
                }
                catch (DomainException failure) when (failure.Code == "refresh_unavailable") { throw; }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                { throw Reauthenticate(); }
            }
            finally { gate.Release(); }
        }
        private static DateTimeOffset? TokenExpiry(string token)
        {
            try
            {
                string[] parts = token.Split('.'); if (parts.Length != 3) return null;
                string payload = parts[1].Replace('-', '+').Replace('_', '/'); payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
                using (JsonDocument document = JsonDocument.Parse(Convert.FromBase64String(payload)))
                { return document.RootElement.TryGetProperty("exp", out JsonElement expiry) ? DateTimeOffset.FromUnixTimeSeconds(expiry.GetInt64()) : null; }
            }
            catch (Exception failure) when (failure is JsonException || failure is FormatException || failure is ArgumentException || failure is InvalidOperationException) { return null; }
        }
        private static DomainException RefreshUnavailable() { return new DomainException(503, "refresh_unavailable", "The Codex login service could not be reached or did not process the refresh. The stored login is unchanged; resume to retry when the service is available."); }
        private static DomainException Reauthenticate() { return new DomainException(409, "reauthentication_required", "Codex login expired or refresh could not be confirmed. Sign in using a dedicated CLI home and replace the stored auth.json. Do not reuse this login in another process. No automatic retry will consume the refresh token again."); }
        private string Encrypt(string id, string value)
        {
            byte[] plaintext = Encoding.UTF8.GetBytes(value); byte[] envelope = new byte[28 + plaintext.Length]; RandomNumberGenerator.Fill(envelope.AsSpan(0, 12));
            try
            {
                using (AesGcm cipher = new AesGcm(_KeyRing.Keys[_KeyRing.ActiveKeyId], 16)) cipher.Encrypt(envelope.AsSpan(0, 12), plaintext, envelope.AsSpan(28), envelope.AsSpan(12, 16), Encoding.UTF8.GetBytes(id));
                return "v1:" + _KeyRing.ActiveKeyId + ":" + Convert.ToBase64String(envelope);
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
        /// <summary>Validate a complete login document and return an isolated run copy without refresh authority.</summary>
        public static string CodexRunCopy(string value)
        {
            JsonObject document = ValidateCodex(value);
            if (document["tokens"] is JsonObject tokens) tokens["refresh_token"] = "";
            return document.ToJsonString();
        }
        private static JsonObject ValidateCodex(string value)
        {
            try
            {
                JsonObject document = JsonNode.Parse(value) as JsonObject ?? throw Invalid();
                string apiKey = document["OPENAI_API_KEY"]?.GetValue<string>() ?? "";
                string access = document["tokens"]?["access_token"]?.GetValue<string>() ?? "";
                if (apiKey.Length == 0 && access.Length == 0) throw Invalid();
                return document;
            }
            catch (Exception failure) when (failure is JsonException || failure is InvalidOperationException || failure is FormatException)
            { throw Invalid(); }
        }
        private static DomainException Invalid() { return new DomainException(400, "invalid_credential", "Provide a token without line breaks, or the complete valid Codex auth.json containing tokens.access_token or OPENAI_API_KEY. Do not include shell prompts or code fences."); }
        /// <inheritdoc />
        public void Dispose() { foreach (byte[] key in _KeyRing.Keys.Values) CryptographicOperations.ZeroMemory(key); _KeyGate.Dispose(); _RefreshHttp.Dispose(); foreach (SemaphoreSlim gate in _RefreshGates.Values) gate.Dispose(); }
    }
}
