namespace ArgueBot.Core.Backups
{
    using ArgueBot.Core.Database;
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;

    internal static class BackupSchema
    {
        internal static Dictionary<string, Dictionary<string, bool>> Tables()
        {
            Dictionary<string, Dictionary<string, bool>> tables = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);
            foreach (Match table in Regex.Matches(SchemaDefinition.Create, @"CREATE TABLE IF NOT EXISTS (\w+)\(([^;\n]+)"))
            {
                Dictionary<string, bool> columns = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (Match column in Regex.Matches(table.Groups[2].Value, @"(?:^|, )([a-z_][a-z0-9_]*) (TEXT|INTEGER|BIGINT)\b")) columns.Add(column.Groups[1].Value, column.Groups[2].Value != "TEXT");
                tables.Add(table.Groups[1].Value, columns);
            }
            return tables;
        }
        internal static Dictionary<string, bool> ColumnsAtVersion(string table, int version)
        {
            Dictionary<string, bool> columns = Tables()[table];
            if (table == "debate_state" && version < 17) columns.Remove("compaction_recipient_id");
            if (table == "debate_state" && version < 24) columns.Remove("rebuild_from_originals");
            if (table == "debate_attempts" && version < 26) columns.Remove("repair");
            if (table == "connection_credentials" && version < 27) columns.Remove("parameter_name");
            if (table == "conversations" && version < 28) columns.Remove("opening_question");
            if ((table == "auth_credentials" || table == "conversation_shares") && version < 31) columns.Remove("token_sealed");
            if (table == "tenants" && version < 30) { columns.Remove("protected"); columns.Remove("revision"); }
            if (table == "users" && version < 30) { columns.Remove("first_name"); columns.Remove("last_name"); columns.Remove("password_sha256"); }
            if (table == "auth_sessions" && version < 30) { columns.Remove("credential_id"); columns.Remove("principal_type"); columns.Remove("auth_scheme"); columns.Remove("token_id"); columns.Remove("source_ip"); columns.Remove("user_agent"); columns.Remove("last_used_utc"); columns.Remove("revoked_utc"); columns.Remove("revocation_reason"); columns.Remove("protected"); }
            if (table == "audit_events" && version < 30) { columns.Remove("principal_type"); columns.Remove("credential_id"); columns.Remove("request_type"); columns.Remove("method"); columns.Remove("path"); columns.Remove("source_ip"); columns.Remove("authentication_result"); columns.Remove("authorization_result"); columns.Remove("reason"); columns.Remove("status_code"); }
            if (table == "connection_endpoints" && version < 16) columns.Remove("max_concurrent_invocations");
            if (table == "connection_profiles" && version < 16) columns.Remove("transport_id");
            if (table == "authorization_user_assignments" && version < 13) columns.Remove("system_kind");
            if (table == "audit_events" && version < 8) { columns.Remove("session_id"); columns.Remove("auth_scheme"); columns.Remove("correlation_id"); columns.Remove("resource_type"); columns.Remove("resource_id"); columns.Remove("error_code"); }
            if (table == "runs" && version < 9) columns.Remove("question");
            if (table == "users" && version < 11) { columns.Remove("password_salt"); columns.Remove("password_iterations"); }
            if (table == "users" && version < 10) { columns.Remove("email"); columns.Remove("password_sha256"); columns.Remove("revision"); }
            if (table == "users" && version < 5) { columns.Remove("is_admin"); columns.Remove("is_tenant_admin"); columns.Remove("protected"); }
            if (table == "backup_operations" && version < 7) { columns.Remove("target_provider"); columns.Remove("target_connection_env"); columns.Remove("retained_staging_home"); }
            if (table == "events" && version < 6) columns.Remove("text_offset");
            if (table == "debate_policies" && version < 33) { columns.Remove("resource_access"); columns.Remove("resource_names_json"); columns.Remove("resource_tags_json"); columns.Remove("max_resource_writes_per_turn"); columns.Remove("max_resource_write_bytes_per_turn"); columns.Remove("max_resources_created"); columns.Remove("resources_created"); }
            if (table == "debate_policies" && version < 35) { foreach (string column in new[] { "user_questions", "intake", "max_intake_questions", "question_hold_seconds", "question_expiry_seconds", "resume_on_answer", "max_question_requests_per_turn", "max_question_requests_per_run", "question_requests" }) columns.Remove(column); }
            if (table == "debate_state" && version < 35) { columns.Remove("waiting_seconds"); columns.Remove("awaiting_since_utc"); }
            if (table == "debate_attempts" && version < 35) { columns.Remove("question_request_id"); columns.Remove("resumes_attempt_id"); }
            return columns;
        }
        internal static int IntroducedVersion(string table)
        { return table == "question_requests" || table == "questions" || table == "question_options" || table == "question_answers" || table == "question_answer_selections" || table == "attempt_continuations" ? 35 : table == "resources" || table == "resource_revisions" || table == "attempt_resources" ? 33 : table == "auth_credentials" || table == "conversation_shares" ? 30 : table == "prompt_templates" || table == "conversation_prompts" || table == "run_prompts" ? 29 : table == "debate_agreement_revisions" ? 25 : table == "connection_environment_owners" || table == "connection_environment_variables" ? 23 : table == "connection_model_generation" || table == "connection_profile_generation" ? 22 : table == "debate_summary_sections" ? 21 : table == "connection_model_checks" ? 20 : table == "attempt_timing" ? 19 : table == "attempt_credentials" ? 18 : table == "debate_policies" || table == "debate_tool_reservations" || table == "debate_attempt_usage" ? 15 : (table.StartsWith("debate_", StringComparison.Ordinal) || table.StartsWith("connection_", StringComparison.Ordinal)) ? 14 : table.StartsWith("authorization_", StringComparison.Ordinal) ? 12 : table == "generation_attempts" || table == "attempt_context" ? 9 : table.StartsWith("backup_", StringComparison.Ordinal) ? 4 : table.StartsWith("request_history", StringComparison.Ordinal) ? 3 : 1; }
        internal static string Normalize(string table, string column)
        { return (table == "messages" || table == "events") && column == "sequence" ? "sequence_number" : column; }
    }
}
