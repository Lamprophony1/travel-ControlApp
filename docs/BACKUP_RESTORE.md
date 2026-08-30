# Backup y restauración

El backup de servidor es distinto de la exportación estructurada JSON. Incluye un snapshot online completo de SQLite y un archivo de claves, adjuntos y contenido privado; la exportación JSON no incluye binarios, usuarios, claves, configuración ni un snapshot restaurable.

`backup-travel-control.sh` aplica `umask 077`, crea cada destino en modo 700 y fija `travel-control.db`, `persistent-files.tar.gz` y `SHA256SUMS` en 600. `verify-backup-artifact.sh` falla si falta un artefacto, si grupo u otros tienen permisos, si el checksum no coincide o si `PRAGMA integrity_check` no devuelve `ok`. Restore ejecuta la misma verificación antes de detener exclusivamente `travel-control`.

Ejecución productiva:

```bash
/opt/travel-control/scripts/backup-travel-control.sh
/opt/travel-control/scripts/restore-travel-control.sh /opt/travel-control/backups/YYYYMMDDTHHMMSSZ
```

El origen de restore debe estar dentro de `/opt/travel-control/backups`. Copiar copias fuera del servidor únicamente a almacenamiento cifrado y con control de acceso equivalente. Tras restaurar, comprobar health local/HTTPS, login, conteos agregados e integridad sin imprimir datos personales ni nombres privados.

CI ejecuta `bash -n scripts/*.sh`, ShellCheck cuando está disponible y `test-backup-permissions.sh`, que construye un SQLite ficticio, valida checksum/integridad y comprueba permisos simbólicos `drwx------` y `-rw-------`.
