namespace ArgueBot.Core.Runners
{
    /// <summary>Bounded local process evidence attached to a runner exception, separate from public error codes.</summary>
    public sealed class RunnerFailureDiagnostics
    {
        /// <summary>Exception data key used between the runner and execution owner.</summary>
        public const string ExceptionDataKey = "ArgueBot.RunnerFailureDiagnostics";
        /// <summary>Maximum retained UTF-16 characters per output field.</summary>
        public const int CaptureCharacters = 16384;
        /// <summary>Provider identifier.</summary>
        public string Provider { get; set; } = "";
        /// <summary>Failure explanation, including parser path when available.</summary>
        public string Detail { get; set; } = "";
        /// <summary>Tail of raw standard output, including non-answer events.</summary>
        public string StandardOutput { get; set; } = "";
        /// <summary>Tail of standard error.</summary>
        public string StandardError { get; set; } = "";
        /// <summary>Tail of the exact event that failed parsing or consistency validation.</summary>
        public string TriggeringRecord { get; set; } = "";
        /// <summary>Whether any output field was truncated.</summary>
        public bool Truncated { get; set; }
        /// <summary>Observed exit status; forced termination may determine this value.</summary>
        public int? ExitCode { get; set; }
    }
}
