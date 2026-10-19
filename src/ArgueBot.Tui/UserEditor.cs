namespace ArgueBot.Tui
{
    using System;
    using System.Net.Mail;
    using System.Security.Cryptography;
    using System.Text;
    using ArgueBot.Core.Models;
    using TUIKit.Widgets;

    internal sealed class UserEditor
    {
        private readonly int _Revision;
        private readonly bool _ExistingAdmin;
        private readonly bool _CanGrantAdmin;
        private readonly TextField _Name = new TextField();
        private readonly TextField _Email = new TextField();
        private readonly TextField _Password = new TextField { MaskChar = '*' };
        private readonly Dropdown<string> _Role = new Dropdown<string>(new[] { "viewer", "operator", "admin" });
        private readonly Checkbox _Active = new Checkbox("Active", true);
        private readonly Checkbox _TenantAdmin = new Checkbox("Tenant administrator", false);
        private readonly Checkbox _Admin = new Checkbox("System administrator", false);
        internal Form Form { get; } = new Form();
        internal UserEditor(bool canGrantAdmin, UserRecord? user = null)
        {
            _CanGrantAdmin = canGrantAdmin; _ExistingAdmin = user?.IsAdmin ?? false; _Revision = user?.Revision ?? 0;
            _Name.Value = user?.Name ?? ""; _Email.Value = user?.Email ?? "";
            _Role.SelectedIndex = Array.IndexOf(new[] { "viewer", "operator", "admin" }, user?.Role ?? "viewer");
            _Active.Checked = user?.Active ?? true; _TenantAdmin.Checked = user?.IsTenantAdmin ?? false; _Admin.Checked = _ExistingAdmin;
            Form.Add("Display name", _Name, () => string.IsNullOrWhiteSpace(_Name.Value) || _Name.Value.Length > 200 ? "Use one through 200 characters." : null);
            Form.SetFieldHint(Form.FieldCount - 1, "Recognizable tenant identity name.");
            Form.Add("Email", _Email, () => _Email.Value.Length <= 254 && MailAddress.TryCreate(_Email.Value, out MailAddress? email) && email.Address == _Email.Value ? null : "Use a valid email address.");
            Form.SetFieldHint(Form.FieldCount - 1, "Unique within this tenant. Used for password login.");
            Form.Add("Domain role", _Role); Form.SetFieldHint(Form.FieldCount - 1, "Viewer reads owned data; operator/admin can modify it. Administrator flags are separate.");
            Form.Add("", _Active); Form.SetFieldHint(Form.FieldCount - 1, "Disabling immediately revokes existing sessions and prevents new logins.");
            Form.Add("", _TenantAdmin); Form.SetFieldHint(Form.FieldCount - 1, "Identity administration within this tenant; no global recovery or service configuration.");
            if (canGrantAdmin) { Form.Add("", _Admin); Form.SetFieldHint(Form.FieldCount - 1, "Full system administration. Only system administrators can grant this flag."); }
            Form.Add("New password", _Password, () => _Password.Value.Length == 0 || (_Password.Value.Length >= 12 && _Password.Value.Length <= 1024) ? null : "Use twelve through 1024 characters, or leave blank.");
            Form.SetFieldHint(Form.FieldCount - 1, "Optional. Blank preserves the password; a new password revokes sessions. Sent as sensitive SHA-256 equivalent.");
        }
        internal UserRequest Extract()
        {
            return new UserRequest { Name = _Name.Value, Email = _Email.Value, Role = _Role.SelectedItem ?? "viewer", Active = _Active.Checked, IsTenantAdmin = _TenantAdmin.Checked, IsAdmin = _CanGrantAdmin ? _Admin.Checked : _ExistingAdmin, ExpectedRevision = _Revision, PasswordSha256 = _Password.Value.Length == 0 ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_Password.Value))) };
        }
    }
}
