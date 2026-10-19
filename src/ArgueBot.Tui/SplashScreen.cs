namespace ArgueBot.Tui
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class SplashScreen
    {
        private const string _Logo = """
             _                         ____        _
            / \   _ __ __ _ _   _  ___| __ )  ___ | |_
           / _ \ | '__/ _` | | | |/ _ \  _ \ / _ \| __|
          / ___ \| | | (_| | |_| |  __/ |_) | (_) | |_
         /_/   \_\_|  \__, |\__,_|\___|____/ \___/ \__|
                      |___/
        """;

        internal static async Task ShowAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Console.Clear();
            Console.WriteLine(_Logo);
            Console.WriteLine();
            Console.WriteLine("Persistent conversations. Thoughtful debates.");
            Console.WriteLine("Press any key to continue (3 seconds).");
            Stopwatch elapsed = Stopwatch.StartNew();
            try
            {
                while (elapsed.Elapsed < TimeSpan.FromSeconds(3))
                {
                    token.ThrowIfCancellationRequested();
                    if (Console.KeyAvailable)
                    {
                        Console.ReadKey(intercept: true);
                        break;
                    }
                    await Task.Delay(20, token).ConfigureAwait(false);
                }
            }
            finally { Console.Clear(); }
        }
    }
}
