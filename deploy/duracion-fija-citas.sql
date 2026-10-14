START TRANSACTION;
ALTER TABLE "CitasServicios" ADD "DuracionMinutos" integer;

UPDATE "CitasServicios" AS cs
SET "DuracionMinutos" = s."DuracionMinutos"
FROM "Servicios" AS s
WHERE cs."ServicioId" = s."Id" AND cs."DuracionMinutos" IS NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261002100527_DuracionFijaCitas', '10.0.2');

COMMIT;
