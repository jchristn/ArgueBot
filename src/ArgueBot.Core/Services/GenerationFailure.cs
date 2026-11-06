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
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                // The provider connection ended after the request was accepted; text received before the drop is kept.
                if (current is System.Net.Http.HttpIOException || (current is System.Net.Http.HttpRequestException request && request.HttpRequestError == System.Net.Http.HttpRequestError.ResponseEnded)) return "provider_disconnected";
                if (current is System.Net.Http.HttpRequestException unreachable && (unreachable.HttpRequestError == System.Net.Http.HttpRequestError.ConnectionError || unreachable.HttpRequestError == System.Net.Http.HttpRequestError.NameResolutionError)) return "provider_unreachable";
            }
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

        /// <summary>Whether a failure is usually intermittent, so a debate can repeat the step automatically within its step retry limit. Credential, configuration, budget and storage failures are not.</summary>
        public static bool Transient(string code)
        {
            ArgumentNullException.ThrowIfNull(code);
            switch (code)
            {
                case "provider_rate_limited": case "provider_unavailable": case "provider_disconnected": case "provider_unreachable": case "refresh_unavailable":
                case "agent_exit": case "io_pipe_closed": case "InvalidDataException": case "generation_timeout": return true;
                default: return false;
            }
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
                case "context_budget_exceeded": return "The prompt or tool results exceed the accepted context budget. No evidence was silently dropped. Inspect summaries and pinned/private sources; use a larger-context model in a new run if the current snapshot cannot fit them.";
                case "invalid_orchestration_output": return "The model did not return the required question, proposal or explicit vote, or voted on an earlier proposal. ArgueBot asks for a corrected reply up to the debate's step retry limit (default 3); when those also fail, the debate pauses here. Inspect the original response and resume to retry, or replace the seat or orchestrator if the model cannot follow this protocol.";
                case "provider_unauthorized": return "The provider refused the credentials (401 or 403). Check the credential or API key bound to the endpoint, and that it is allowed to use this model, then resume.";
                case "provider_rate_limited": return "The provider is rate limiting requests (429). ArgueBot retried the step automatically up to the debate's step retry limit, waiting longer each time. Wait before resuming; providers usually ask for a few seconds to a few minutes, and your plan's quota may need to reset.";
                case "provider_unavailable": return "The provider failed with a server error (5xx). It may be overloaded or restarting. ArgueBot retried the step automatically up to the debate's step retry limit; resume later.";
                case "provider_disconnected": return "The provider closed the connection in the middle of the answer. Text received before the drop is kept on the failed turn. Resume to retry the turn; it may repeat work the provider already did.";
                case "provider_unreachable": return "ArgueBot could not connect to the provider endpoint. Check that the endpoint is running and its base URL is correct, then resume.";
                case "provider_error": return "The provider rejected the request. Check the endpoint, model name and credentials in Connections and models, then resume.";
                case "refresh_unavailable": return "The Codex login service could not be reached, was rate limited or failed before refreshing the login. The stored login is unchanged and still usable. Resume to retry when the service is available.";
                case "summary_validation_failed": return "The summary failed validation (missing or invented sources, a missing section, an unattributed pinned constraint, or size) and was not activated. The previous summary and all original turns remain available. Inspect the response, then resume to retry or rebuild the summary.";
                case "reauthentication_required":
                case "credential_unavailable": return "The configured credential is disabled, expired or uncertain after refresh. Import a fresh value through Connections and models, then explicitly resume. Do not share a refreshing Codex login between processes.";
                case "credentialed_generation_failed": return "The provider invocation failed. Check its endpoint, model and imported login before retrying. Credential-bearing diagnostics are withheld to avoid exposing secrets.";
                case "debate_budget_exhausted": return "A phase or estimated-input budget is exhausted. Inspect run usage and start a continuation with reviewed limits; retry does not reset usage.";
                case "tool_budget_exhausted": return "The run reached its tool-call budget. Start a continuation with reviewed limits.";
                case "wall_clock_budget_exhausted": return "The run reached its elapsed deadline, including queued and paused time. Start a continuation with a new deadline.";
                case "generation_limit": return "This run reached its total invocation limit. Review its outcome and start a continuation; resuming cannot reset that limit.";
                case "generation_timeout": return "The agent reached this run's per-turn time limit and was stopped, even if it was still working. Increase Timeout seconds in Participants, then start a new debate. Resume keeps the original timeout and may repeat external work.";
                case "generation_interrupted": return "The current generation was explicitly interrupted. Partial text is retained. Resume retries this turn with its original settings and may repeat external work.";
                case "generation_stopped": return "The run was explicitly stopped. Partial text is retained. External work may already have occurred; inspect it before starting another debate.";
                case "service_shutdown": return "The service stopped during generation. Partial text is retained. Inspect external work before resuming; the retry keeps the original settings and may repeat work.";
                case "interrupted": return "Generation was interrupted. The original cause was not recorded; this can follow an explicit interrupt or service shutdown. Inspect local runner diagnostics and service logs before retrying. Resume may repeat external work and keeps the original timeout.";
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
