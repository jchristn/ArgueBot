# Backup and recovery during replacement development

The replacement service supports SQLite and PostgreSQL snapshots. Complete recovery archives can be created from either provider. The controlled complete-archive restore currently creates a new SQLite home. Direct complete-archive restoration into PostgreSQL, backup upload/import, retention scheduling and complete terminal UI parity remain open in IMPROVEMENTS.md. Keep the legacy application available until the product gates pass.

Open Dashboard > Backups as a system administrator. Create an archive with a recognizable name. Supply an optional passphrase of at least twelve characters to encrypt it. Selecting local keys and connection credentials requires encryption; environment secret values are never included. Keep the passphrase separately. The service cannot recover it. Without encryption, an archive contains readable conversation and request history and must be kept private.

Creation returns an operation identifier immediately. The service continues after the browser or console disconnects. Poll its status until completed or failed. A ready artifact records its byte length and SHA-256 digest. Download uses the authenticated connection, with no token in the URL. Verify checks decryption, the strict inventory, checksums and snapshot/schema compatibility without changing live data. A failed verify does not destroy the original artifact. Missing environment credential references are reported by name for operator reconfiguration.

Restore requires a new home name and listener port. The destination is beneath the running service home's restores directory. Existing destinations are refused. The service validates the whole archive before restoring the relational snapshot, reconciles uncertain work, revokes restored sessions and leaves the recovered service stopped. Inspect retained source settings, restore required environment credentials and deliberately start that separate home when ready. The active service is unchanged.

The console exposes the same operations. Passphrases should come from stdin or an owner-readable request file, rather than shell history. For example, from the repository root with an isolated development service home:

```sh
printf '%s' '{"name":"Before configuration changes","includeSecrets":false}' |
  dotnet run --project src/ArgueBot.Cli -c Release -f net10.0 -- --home .run/home backups create -
dotnet run --project src/ArgueBot.Cli -c Release -f net10.0 -- --home .run/home backups list
```

Use the returned identifiers in `backups status OPERATION`, `backups download BACKUP NEW_FILE`, `backups verify BACKUP --file REQUEST_FILE`, `backups restore BACKUP --file REQUEST_FILE` and `backups delete BACKUP`. A verify request is `{}` for an unencrypted archive or an object with `password`. A restore request also needs `destinationName` and optionally `port`. Acceptance is not completion: inspect the durable operation result. Interrupted jobs fail visibly and require an explicit new request; they are never silently replayed.

The offline server maintenance flags remain useful for database-only snapshots: `--export-snapshot FILE`, `--verify-snapshot FILE`, and `--home EXPLICIT_HOME --restore-snapshot FILE`. Snapshot restore is transactional and keeps a pre-restore snapshot. These files omit service settings and keys. Never treat them as complete-home archives or run offline mutation beside an active service. See [operations](OPERATIONS.md) and [API reference](../REST_API.md).
