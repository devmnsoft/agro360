DO $$ BEGIN
 IF current_setting('server_version_num')::integer < 140000 THEN RAISE EXCEPTION 'Agro 360 requer PostgreSQL 14 ou superior'; END IF;
 IF NOT EXISTS (SELECT FROM pg_extension WHERE extname='postgis') THEN RAISE EXCEPTION 'PostGIS ausente: solicite CREATE EXTENSION postgis ao administrador'; END IF;
 IF NOT EXISTS (SELECT FROM information_schema.tables WHERE table_schema='platform' AND table_name='schema_migrations') THEN RAISE EXCEPTION 'Schema Agro 360 não instalado'; END IF;
END $$;
