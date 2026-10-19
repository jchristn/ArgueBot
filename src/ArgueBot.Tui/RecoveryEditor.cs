namespace ArgueBot.Tui
{
    using System;
    using System.Globalization;
    using System.Text;
    using System.Text.RegularExpressions;
    using ArgueBot.Core.Models;
    using TUIKit.Widgets;

    internal sealed class RecoveryEditor
    {
        private readonly string _Kind;
        private readonly TextField _Name = new TextField { Value = "Recovery archive" };
        private readonly TextField _Password = new TextField { MaskChar = '*' };
        private readonly TextField _Destination = new TextField();
        private readonly TextField _Port = new TextField { Value = "4537" };
        private readonly Checkbox _Secrets = new Checkbox("Include local keys and literal connection credentials", false);
        internal Form Form { get; } = new Form();
        internal RecoveryEditor(string kind, bool encrypted = false)
        {
            _Kind = kind;
            if (kind == "create")
            {
                Form.Add("Archive name", _Name, () => string.IsNullOrWhiteSpace(_Name.Value) || _Name.Value.Length > 200 ? "Use one through 200 characters." : null);
                Form.SetFieldHint(Form.FieldCount - 1, "Recognizable label for the service-owned recovery artifact.");
                Form.Add("", _Secrets); Form.SetFieldHint(Form.FieldCount - 1, "Requires a passphrase. Environment secret values are never copied.");
            }
            Form.Add("Passphrase", _Password, () =>
            {
                string password = _Password.Value;
                if (password.Length == 0) return encrypted || (kind == "create" && _Secrets.Checked) ? "This archive requires a passphrase." : null;
                return password.Length >= 12 && Encoding.UTF8.GetByteCount(password) <= 1024 ? null : "Use at least twelve characters and at most 1024 UTF-8 bytes.";
            });
            Form.SetFieldHint(Form.FieldCount - 1, kind == "create" ? "Optional encryption unless keys are included. Keep this passphrase separately; it cannot be recovered." : "Required for an encrypted archive. The job never persists the passphrase.");
            if (kind == "restore")
            {
                Form.Add("New home name", _Destination, () => Regex.IsMatch(_Destination.Value, "^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant) ? null : "Use one to 64 letters, digits, hyphens or underscores.");
                Form.SetFieldHint(Form.FieldCount - 1, "New SQLite home beneath the service home's restores directory. Existing homes are never replaced; the recovered service stays stopped.");
                Form.Add("Recovered listener port", _Port, () => int.TryParse(_Port.Value, out int value) && value >= 1024 && value <= 65535 ? null : "Use 1024 through 65535.");
                Form.SetFieldHint(Form.FieldCount - 1, "Choose a port for the recovered home. Restoration does not start a listener.");
            }
        }
        internal BackupRequest Extract()
        {
            return new BackupRequest { Name = _Name.Value, Password = _Password.Value, IncludeSecrets = _Kind == "create" && _Secrets.Checked, DestinationName = _Destination.Value, Port = _Kind == "restore" ? int.Parse(_Port.Value, CultureInfo.InvariantCulture) : 0 };
        }
    }
}
