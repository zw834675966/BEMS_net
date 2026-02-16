SELECT extname
FROM pg_extension
WHERE extname = 'timescaledb';

SELECT schemaname, tablename
FROM pg_tables
WHERE schemaname = 'bems' AND tablename = 'telemetry_demo';

INSERT INTO bems.telemetry_demo (value) VALUES (99.9);

SELECT id, value, created_at
FROM bems.telemetry_demo
ORDER BY id DESC
LIMIT 1;
