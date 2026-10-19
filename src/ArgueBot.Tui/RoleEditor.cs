namespace ArgueBot.Tui
{
    using System;
    using ArgueBot.Core.Models;
    using TUIKit.Widgets;

    internal sealed class RoleEditor
    {
        private readonly int _Revision;
        private readonly string _CloneRoleId;
        private readonly TextField _Name = new TextField();
        private readonly Checkbox _Active = new Checkbox("Role enabled", true);
        internal Form Form { get; } = new Form();
        internal RoleEditor(RoleRecord? role = null, bool clone = false)
        {
            if (!clone && role != null && (role.Protected || role.Builtin)) throw new ArgumentException("Clone a protected definition before customizing it.", nameof(role));
            _Revision = clone ? 0 : role?.Revision ?? 0; _CloneRoleId = clone ? role?.Id ?? "" : "";
            _Name.Value = role == null ? "" : clone ? role.Name + " copy" : role.Name; _Active.Checked = clone || (role?.Active ?? true);
            Form.Add("Role name", _Name, () => string.IsNullOrWhiteSpace(_Name.Value) || _Name.Value.Length > 200 ? "Use one through 200 characters; built-in names are reserved." : null);
            Form.SetFieldHint(Form.FieldCount - 1, "Recognizable custom name. Cloning copies permission mappings without editing the source.");
            Form.Add("", _Active); Form.SetFieldHint(Form.FieldCount - 1, "Disabling stops the role contributing permissions while retaining assignments for history.");
        }
        internal RoleRequest Extract()
        { return new RoleRequest { Name = _Name.Value, Active = _Active.Checked, ExpectedRevision = _Revision, CloneRoleId = _CloneRoleId }; }
    }
}
