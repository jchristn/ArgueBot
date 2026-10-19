namespace ArgueBot.Client
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Models;

    /// <summary>Starts or attaches to the service for a local home, without opening its database.</summary>
    public static class LocalServiceConnector
    {
        /// <summary>Resolve an explicit remote/local address, or attach/start the selected local home.</summary>
        public static async Task<string> ConnectAsync(string home, string? endpoint = null, bool autoStart = true, CancellationToken token = default)
        {
            if (endpoint != null) return endpoint;
            ArgueBotPaths paths = new ArgueBotPaths(home);
            string descriptorFile = Path.Combine(paths.Home, "runtime", "service.json");
            using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) })
            {
                if (File.Exists(descriptorFile))
                {
                    LocalServiceDescriptor? descriptor = JsonSerializer.Deserialize<LocalServiceDescriptor>(await File.ReadAllTextAsync(descriptorFile, token).ConfigureAwait(false));
                    if (descriptor != null && IsLocal(descriptor.Endpoint) && await ReadyAsync(http, descriptor.Endpoint, token).ConfigureAwait(false))
                    {
                        if (descriptor.Protocol != 1) throw new InvalidOperationException("The running local service uses an incompatible attachment protocol. Update the client and service together.");
                        return descriptor.Endpoint;
                    }
                }
                int port = 4317;
                if (File.Exists(paths.SettingsFile))
                {
                    JsonSerializerOptions options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    options.Converters.Add(new JsonStringEnumConverter());
                    ServiceSettings? settings = JsonSerializer.Deserialize<ServiceSettings>(await File.ReadAllTextAsync(paths.SettingsFile, token).ConfigureAwait(false), options);
                    port = settings?.Port ?? port;
                }
                string local = "http://127.0.0.1:" + port;
                if (await ReadyAsync(http, local, token).ConfigureAwait(false)) return local;
                if (!autoStart) throw new InvalidOperationException("The local service is not running. Start ArgueBot.Server or omit --no-start.");
                ProcessStartInfo start = LocateServer();
                start.ArgumentList.Add("--home"); start.ArgumentList.Add(paths.Home); start.ArgumentList.Add("--detached");
                start.UseShellExecute = false; start.CreateNoWindow = true;
                start.RedirectStandardInput = true; start.RedirectStandardOutput = true; start.RedirectStandardError = true;
                using (Process process = Process.Start(start) ?? throw new IOException("The local service process could not start."))
                {
                    process.StandardInput.Close();
                    DateTime deadline = DateTime.UtcNow.AddSeconds(20);
                    while (DateTime.UtcNow < deadline)
                    {
                        token.ThrowIfCancellationRequested();
                        if (await ReadyAsync(http, local, token).ConfigureAwait(false)) return local;
                        await Task.Delay(100, token).ConfigureAwait(false);
                    }
                    throw new TimeoutException("The local service did not become ready within twenty seconds. Inspect the application home's startup log.");
                }
            }
        }
        private static bool IsLocal(string endpoint)
        {
            return Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) && uri.Scheme == "http" && uri.Host == "127.0.0.1" && uri.AbsolutePath == "/" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;
        }
        private static async Task<bool> ReadyAsync(HttpClient http, string endpoint, CancellationToken token)
        {
            try
            {
                HealthResponse? response = await http.GetFromJsonAsync<HealthResponse>(endpoint + "/health", token).ConfigureAwait(false);
                if (response?.Status != "ready" || response.Schema < 1) return false;
                if (response.Protocol != 1) throw new InvalidOperationException("The listener uses an incompatible attachment protocol. Start matching client and service builds together.");
                return true;
            }
            catch (HttpRequestException) { return false; }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return false; }
            catch (JsonException) { return false; }
        }
        private static ProcessStartInfo LocateServer()
        {
            string executable = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "ArgueBot.Server.exe" : "ArgueBot.Server");
            if (File.Exists(executable)) return new ProcessStartInfo(executable);
            string assembly = Path.Combine(AppContext.BaseDirectory, "ArgueBot.Server.dll");
            if (!File.Exists(assembly))
            {
                DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory != null)
                {
                    string project = Path.Combine(directory.FullName, "src", "ArgueBot.Server", "ArgueBot.Server.csproj");
                    if (File.Exists(project))
                    {
                        assembly = Path.Combine(directory.FullName, "src", "ArgueBot.Server", "bin", "Release", "net10.0", "ArgueBot.Server.dll");
                        break;
                    }
                    directory = directory.Parent;
                }
            }
            if (!File.Exists(assembly)) throw new FileNotFoundException("ArgueBot.Server is not installed beside this client. Build the solution in Release or start a service explicitly with --server.");
            ProcessStartInfo start = new ProcessStartInfo("dotnet"); start.ArgumentList.Add(assembly); return start;
        }
    }
}
