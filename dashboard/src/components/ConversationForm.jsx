import { useState } from "react";
import { Button, Field, Modal } from "./ui.jsx";

/** Create a conversation (title plus optional opening question) or rename one (title only). */
export default function ConversationForm({ initial = "", onSave, onClose }) {
  const renaming = Boolean(initial);
  const [title, setTitle] = useState(initial);
  const [question, setQuestion] = useState("");
  const [busy, setBusy] = useState(false);
  return (
    <Modal title={renaming ? "conversations.rename" : "conversations.new"} onClose={onClose}>
      <form
        onSubmit={async (event) => {
          event.preventDefault();
          if (busy || !title.trim()) return;
          setBusy(true);
          try {
            await onSave(title.trim(), question.trim());
          } finally {
            setBusy(false);
          }
        }}
      >
        <Field label="conversations.titleField">
          {(props) => <input {...props} data-autofocus required maxLength="200" value={title} onChange={(event) => setTitle(event.target.value)} />}
        </Field>
        {!renaming && <Field label="conversations.openingQuestion">
          {(props) => <textarea {...props} rows={5} maxLength="4000" value={question} onChange={(event) => setQuestion(event.target.value)} />}
        </Field>}
        <footer className="dialog-actions">
          <Button label="common.cancel" help="common.cancel" type="button" onClick={onClose} />
          <Button label={renaming ? "common.save" : "conversations.create"} help="conversations.new.help" className="primary" type="submit" disabled={busy || !title.trim()} />
        </footer>
      </form>
    </Modal>
  );
}
