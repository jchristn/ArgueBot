# Open workspace

ArgueBot has no product accounts, roles, permissions, login, pairing, sessions or security audit surface. Its service, web dashboard, TUI and CLI operate on one open workspace. An old `requireAuthentication` configuration field is ignored and cannot enable sign-in. The public API omits identity routes and schemas.

Host/origin checks, local-home process verification, file permissions, TLS support and service-owned execution remain operational safeguards. They do not identify product users. Agent providers can still require their own API credentials or harness sign-in.

Existing database ownership columns and historical identity tables remain solely to preserve stored conversations and schema migration compatibility. They have no product administration endpoints or menus. Local execution does not depend on active accounts or role assignments. HTTP diagnostics retain request IDs, trace IDs, redacted headers and bodies, without exposing principal or tenant fields.

Account, session, permission management and authorization watchers have been removed from the runtime. Historical tables remain inert and are not deleted or reseeded at startup. The schema-11 legacy verifier conversion remains only as a historical migration step, with no login or verification API.
