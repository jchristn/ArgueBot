import { useState } from "react";
import { Button, Field, Modal } from "./ui.jsx";
export default function ConversationForm({ initial = "", onSave, onClose }) {
  const [title, setTitle] = useState(initial);
  const [busy, setBusy] = useState(false);
  return (
    <Modal
      title={initial ? "conversations.rename" : "conversations.new"}
      onClose={onClose}
    >
      <form
        onSubmit={async (event) => {
          event.preventDefault();
          if (busy || !title.trim()) return;
          setBusy(true);
          try {
            await onSave(title.trim());
          } finally {
            setBusy(false);
          }
        }}
      >
        <Field label="conversations.topic">
          {(props) => (
            <textarea
              {...props}
              rows="5"
              autoFocus
              required
              maxLength="4000"
              value={title}
              onChange={(event) => setTitle(event.target.value)}
            />
          )}
        </Field>
        <footer>
          <Button
            label="common.cancel"
            help="common.cancel"
            type="button"
            onClick={onClose}
          />
          <Button
            label="common.save"
            help="conversations.new.help"
            className="primary"
            type="submit"
            disabled={busy || !title.trim()}
          />
        </footer>
      </form>
    </Modal>
  );
}
