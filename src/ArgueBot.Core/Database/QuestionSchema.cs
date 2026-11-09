namespace ArgueBot.Core.Database
{
    /// <summary>Questions agents ask the user: requests, their questions and options, answers and selected options, and the stored continuation a parked turn resumes from.</summary>
    internal static class QuestionSchema
    {
        internal const string Create = """
CREATE TABLE IF NOT EXISTS question_requests(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, conversation_id TEXT NOT NULL, run_id TEXT NOT NULL, attempt_id TEXT NOT NULL DEFAULT '', call_id TEXT NOT NULL, digest TEXT NOT NULL DEFAULT '', asker_profile_id TEXT NOT NULL, asker_name TEXT NOT NULL DEFAULT '', asker_role TEXT NOT NULL DEFAULT 'participant', phase TEXT NOT NULL DEFAULT '', round BIGINT NOT NULL DEFAULT 0, context TEXT NOT NULL DEFAULT '', wait INTEGER NOT NULL DEFAULT 1, visibility TEXT NOT NULL DEFAULT 'panel', status TEXT NOT NULL DEFAULT 'open', hold_state TEXT NOT NULL DEFAULT 'none', resolution_reason TEXT NOT NULL DEFAULT '', resolution_note TEXT NOT NULL DEFAULT '', resolved_by_user_id TEXT NOT NULL DEFAULT '', resolution_key TEXT NOT NULL DEFAULT '', answer_message_id TEXT NOT NULL DEFAULT '', resume_attempt_id TEXT NOT NULL DEFAULT '', credential_warning INTEGER NOT NULL DEFAULT 0, expires_utc TEXT NOT NULL DEFAULT '', resolved_utc TEXT NOT NULL DEFAULT '', active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, UNIQUE(run_id,call_id), UNIQUE(tenant_id,id), FOREIGN KEY(tenant_id,run_id) REFERENCES runs(tenant_id,id) ON DELETE CASCADE);
CREATE INDEX IF NOT EXISTS ix_question_requests_status ON question_requests(tenant_id,status,created_utc,id);
CREATE INDEX IF NOT EXISTS ix_question_requests_run ON question_requests(tenant_id,run_id,status);
CREATE INDEX IF NOT EXISTS ix_question_requests_conversation ON question_requests(tenant_id,conversation_id,created_utc);
CREATE TABLE IF NOT EXISTS questions(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, request_id TEXT NOT NULL, position BIGINT NOT NULL, question_key TEXT NOT NULL, header TEXT NOT NULL, prompt TEXT NOT NULL, kind TEXT NOT NULL, allow_other INTEGER NOT NULL DEFAULT 0, min_selections BIGINT NOT NULL DEFAULT 0, max_selections BIGINT NOT NULL DEFAULT 0, max_length BIGINT NOT NULL DEFAULT 0, placeholder TEXT NOT NULL DEFAULT '', required INTEGER NOT NULL DEFAULT 1, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, UNIQUE(request_id,position), UNIQUE(request_id,question_key), UNIQUE(tenant_id,id), FOREIGN KEY(tenant_id,request_id) REFERENCES question_requests(tenant_id,id) ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS question_options(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, question_id TEXT NOT NULL, position BIGINT NOT NULL, option_key TEXT NOT NULL, label TEXT NOT NULL, description TEXT NOT NULL DEFAULT '', recommended INTEGER NOT NULL DEFAULT 0, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, UNIQUE(question_id,position), UNIQUE(question_id,option_key), UNIQUE(tenant_id,id), FOREIGN KEY(tenant_id,question_id) REFERENCES questions(tenant_id,id) ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS question_answers(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, question_id TEXT NOT NULL UNIQUE, skipped INTEGER NOT NULL DEFAULT 0, text TEXT NOT NULL DEFAULT '', other_text TEXT NOT NULL DEFAULT '', confirmed INTEGER NOT NULL DEFAULT -1, answered_by_user_id TEXT NOT NULL DEFAULT '', active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, UNIQUE(tenant_id,id), FOREIGN KEY(tenant_id,question_id) REFERENCES questions(tenant_id,id) ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS question_answer_selections(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, answer_id TEXT NOT NULL, option_id TEXT NOT NULL, position BIGINT NOT NULL, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, UNIQUE(answer_id,option_id), FOREIGN KEY(tenant_id,answer_id) REFERENCES question_answers(tenant_id,id) ON DELETE CASCADE, FOREIGN KEY(tenant_id,option_id) REFERENCES question_options(tenant_id,id) ON DELETE CASCADE);
CREATE INDEX IF NOT EXISTS ix_events_tenant_kind ON events(tenant_id,kind,created_utc,id);
CREATE TABLE IF NOT EXISTS attempt_continuations(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, attempt_id TEXT NOT NULL UNIQUE, request_id TEXT NOT NULL, transport TEXT NOT NULL, pending_call_id TEXT NOT NULL DEFAULT '', session_id TEXT NOT NULL DEFAULT '', raw_continuation_json TEXT NOT NULL DEFAULT '', active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, FOREIGN KEY(tenant_id,attempt_id) REFERENCES generation_attempts(tenant_id,id) ON DELETE CASCADE);
""";

        /// <summary>Columns added to existing tables. Upgraded debates keep intake off; every seat may still ask, as it could with clarification requests.</summary>
        internal const string Columns = """
ALTER TABLE debate_policies ADD COLUMN user_questions TEXT NOT NULL DEFAULT 'all';
ALTER TABLE debate_policies ADD COLUMN intake TEXT NOT NULL DEFAULT 'off';
ALTER TABLE debate_policies ADD COLUMN max_intake_questions BIGINT NOT NULL DEFAULT 4;
ALTER TABLE debate_policies ADD COLUMN question_hold_seconds BIGINT NOT NULL DEFAULT 300;
ALTER TABLE debate_policies ADD COLUMN question_expiry_seconds BIGINT NOT NULL DEFAULT 0;
ALTER TABLE debate_policies ADD COLUMN resume_on_answer INTEGER NOT NULL DEFAULT 1;
ALTER TABLE debate_policies ADD COLUMN max_question_requests_per_turn BIGINT NOT NULL DEFAULT 2;
ALTER TABLE debate_policies ADD COLUMN max_question_requests_per_run BIGINT NOT NULL DEFAULT 20;
ALTER TABLE debate_policies ADD COLUMN question_requests BIGINT NOT NULL DEFAULT 0;
ALTER TABLE debate_state ADD COLUMN waiting_seconds BIGINT NOT NULL DEFAULT 0;
ALTER TABLE debate_state ADD COLUMN awaiting_since_utc TEXT NOT NULL DEFAULT '';
ALTER TABLE debate_attempts ADD COLUMN question_request_id TEXT NOT NULL DEFAULT '';
ALTER TABLE debate_attempts ADD COLUMN resumes_attempt_id TEXT NOT NULL DEFAULT '';
""";

        /// <summary>Move every clarification request into a one-question text request, keep its answer, park open blocking requests and drop the old table.</summary>
        internal const string MigrateClarifications = """
INSERT INTO question_requests(id,tenant_id,conversation_id,run_id,attempt_id,call_id,asker_profile_id,asker_role,wait,visibility,status,hold_state,resolution_reason,answer_message_id,active,created_utc,updated_utc,resolved_utc) SELECT 'qrq_' || substr(id,5),tenant_id,conversation_id,run_id,'',call_id,profile_id,'participant',blocking,'panel',status,CASE WHEN status='open' AND blocking=1 THEN 'parked' ELSE 'none' END,CASE WHEN status='skipped' THEN 'skipped_by_user' ELSE '' END,answer_message_id,active,created_utc,updated_utc,CASE WHEN status='open' THEN '' ELSE updated_utc END FROM debate_clarifications;
INSERT INTO questions(id,tenant_id,request_id,position,question_key,header,prompt,kind,max_length,required,active,created_utc,updated_utc) SELECT 'qst_' || substr(id,5),tenant_id,'qrq_' || substr(id,5),0,'question','Question',question,'text',8000,1,active,created_utc,updated_utc FROM debate_clarifications;
INSERT INTO question_answers(id,tenant_id,question_id,skipped,text,active,created_utc,updated_utc) SELECT 'qan_' || substr(c.id,5),c.tenant_id,'qst_' || substr(c.id,5),CASE WHEN c.status='skipped' THEN 1 ELSE 0 END,CASE WHEN c.status='answered' THEN COALESCE((SELECT m.content FROM messages m WHERE m.tenant_id=c.tenant_id AND m.id=c.answer_message_id),'') ELSE '' END,c.active,c.updated_utc,c.updated_utc FROM debate_clarifications c WHERE c.status IN ('answered','skipped');
UPDATE runs SET state='awaiting_input',outcome='' WHERE state='paused' AND outcome='awaiting_clarification';
DROP TABLE debate_clarifications;
""";
    }
}
