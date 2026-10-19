namespace ArgueBot.Tui
{
    using System;
    using System.Globalization;
    using ArgueBot.Core.Models;
    using TUIKit.Widgets;

    internal sealed class AuditDetails
    {
        internal Form Form { get; } = new Form();
        internal AuditDetails(AuditEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            Add("Event", entry.Id, "Immutable security event identifier.");
            Add("UTC time", entry.CreatedUtc.ToString("O", CultureInfo.InvariantCulture), "Event time, without a local timezone conversion.");
            Add("Operation", entry.Operation, "Stable semantic operation code.");
            Add("Outcome", entry.Outcome, "Recorded result. Denials remain visible independently of request capture.");
            Add("Tenant", entry.TenantId ?? "", "Validated tenant. Empty for anonymous or system events.");
            Add("Actor", entry.UserId ?? "", "Validated actor. Untrusted user hints are excluded.");
            Add("Session", entry.SessionId, "Session identifier only; token material is never included.");
            Add("Authentication", entry.AuthScheme, "Validated authentication scheme where known.");
            Add("Correlation", entry.CorrelationId, "Use this identifier to correlate request diagnostics.");
            Add("Resource type", entry.ResourceType, "Type of the security event target.");
            Add("Resource", entry.ResourceId, "Target identifier where known.");
            Add("Error code", entry.ErrorCode, "Safe failure code without credentials or request bodies.");
        }
        private void Add(string label, string value, string hint)
        { Form.Add(label, new TextField { Value = value, IsReadOnly = true }); Form.SetFieldHint(Form.FieldCount - 1, hint); }
    }
}
