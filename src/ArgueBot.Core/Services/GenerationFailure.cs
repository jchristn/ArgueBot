namespace ArgueBot.Core.Services
{
    using System;
    using System.IO;

    /// <summary>Safe, actionable generation failures without raw process or provider payloads.</summary>
    public static class GenerationFailure
    {
        /// <summary>Classify known I/O failures while preserving domain error codes.</summary>
        public static string Code(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            if (exception is DomainException domain) return domain.Code;
            if (exception is IOException)
            {
                string known = CaptureCode(exception.Message);
                if (known.Length > 0) return known;
                switch (unchecked((uint)exception.HResult))
                {
                    case 0x80070027: case 0x80070070: return "io_disk_full";
                    case 0x8007006d: case 0x800700e8: return "io_pipe_closed";
                }
            }
            return exception.GetType().Name;
        }

        /// <summary>Recognize only exact application-generated capture errors in historical diagnostics.</summary>
        public static string CaptureCode(string detail)
        {
            switch (detail)
            {
                case "Agent stdout exceeded the configured capture limit.": return "agent_stdout_limit";
                case "Agent stderr exceeded the configured capture limit.": return "agent_stderr_limit";
                case "Provider answer exceeded the configured capture limit.": return "provider_output_limit";
                default: return "";
            }
        }

        /// <summary>Describe the cause and recovery, explicitly respecting frozen run configuration.</summary>
        public static string Explain(string code)
        {
            switch (code)
            {
                case "agent_stdout_limit": return "Agent standard output exceeded this run's output byte limit. CLI protocol events and tool results count toward this limit, even when the visible answer is short. Increase Output byte limit in Agents or reduce tool output, then stop this run and start a new debate. Resume keeps the original limit.";
                case "agent_stderr_limit": return "Agent standard error exceeded this run's output byte limit. Reduce verbose logging or increase Output byte limit in Agents, then stop this run and start a new debate. Resume keeps the original limit.";
                case "provider_output_limit": return "The provider answer exceeded this run's output byte limit. Request a shorter answer or increase Output byte limit in Agents, then stop this run and start a new debate. Resume keeps the original limit.";
                case "io_disk_full": return "An input/output operation failed because the device has no free space. Free disk space on the service host and check its logs before retrying. Partial external work may already have occurred.";
                case "io_pipe_closed": return "The agent process closed its input/output pipe before generation finished. Check the agent's exit status, authentication and local runner-failure diagnostics before retrying. Partial external work may already have occurred.";
                case "IOException": return "An input/output operation failed while communicating with the agent or accessing local storage. The exact cause was not recorded in this attempt. Inspect its local logs/runner-failures diagnostic and service logs for the I/O detail; check disk space and whether the agent exited before retrying. Resume retains the original agent settings.";
                case "Win32Exception": return "The agent program could not start. In Agents, check the executable and working directory; put flags in separate Argument rows. Stop this run and start a new debate after correcting settings. Resume retains the original settings.";
                case "InvalidDataException": return "The agent returned malformed or conflicting output. Inspect the attempt's local runner-failure diagnostic, check the configured CLI protocol and agent version, then retry after correcting the cause.";
                case "agent_exit": return "The agent process exited unsuccessfully. Inspect its exit status and standard error in the local runner-failure diagnostic; check authentication and agent configuration before retrying.";
                case "": return "";
                default: return "Generation failed. Inspect generation attempts and the service's local runner-failure diagnostics for the cause before retrying. Resume retains the original agent settings.";
            }
        }
    }
}
