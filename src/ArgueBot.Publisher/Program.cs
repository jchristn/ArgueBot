namespace ArgueBot.Publisher
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            string root = Directory.GetCurrentDirectory(); string channel = "archive"; string runtime = RuntimeInformation.RuntimeIdentifier; bool doctor = false;
            for (int index = 0; index < args.Length; index++)
            {
                if (args[index] == "--root" && index + 1 < args.Length) root = args[++index];
                else if (args[index] == "--channel" && index + 1 < args.Length) channel = args[++index];
                else if (args[index] == "--runtime" && index + 1 < args.Length) runtime = args[++index];
                else if (args[index] == "--doctor") doctor = true;
                else { Console.WriteLine("ArgueBot.Publisher [--root REPOSITORY] [--runtime RID] [--channel archive|inno|pkg|deb|rpm|appimage] [--doctor]. Builds only; never publishes or changes versions."); return args[index] == "--help" ? 0 : 2; }
            }
            using (CancellationTokenSource lifetime = new CancellationTokenSource())
            {
                Console.CancelKeyPress += (_, received) => { received.Cancel = true; lifetime.Cancel(); };
                try
                {
                    PublisherManifest manifest = JsonSerializer.Deserialize<PublisherManifest>(await File.ReadAllTextAsync(Path.Combine(root, "publisher.json"), lifetime.Token).ConfigureAwait(false), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("The publisher manifest is empty.");
                    if (doctor)
                    {
                        new PackageBuilder(root, manifest).Validate(runtime, channel);
                        List<string> problems = await PackageDoctor.InspectAsync(root, channel, lifetime.Token).ConfigureAwait(false);
                        foreach (string problem in problems) Console.WriteLine(problem);
                        if (problems.Count == 0) Console.WriteLine("Packaging prerequisites passed. Native installation and publication remain separate checks.");
                        return problems.Count == 0 ? 0 : 1;
                    }
                    string artifact = await new PackageBuilder(root, manifest).BuildAsync(runtime, channel, lifetime.Token).ConfigureAwait(false);
                    Console.WriteLine("Built " + artifact + ". SHA256SUMS records its digest. Installation and publication are separate checks."); return 0;
                }
                catch (OperationCanceledException) { return 130; }
                catch (Exception failure) when (failure is not OutOfMemoryException) { Console.Error.WriteLine(failure.Message); return 1; }
            }
        }
    }
}
