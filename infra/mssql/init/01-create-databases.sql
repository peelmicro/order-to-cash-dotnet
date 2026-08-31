-- Order-To-Cash — MS-SQL Server bootstrap.
--
-- Creates one database per service (database-per-service, no cross-database
-- joins, no foreign keys crossing service boundaries — see CLAUDE.md), and
-- creates the application login with db_owner in each of them.
--
-- The #7 counterpart of this file is infra/mysql/init/01-create-databases.sh,
-- which the MySQL image ran for us out of /docker-entrypoint-initdb.d. The
-- MS-SQL image has no such directory and no init hook of any kind: it starts
-- sqlservr as PID 1 and nothing else. infra/mssql/entrypoint.sh therefore
-- supplies the missing mechanism, and this file is what it runs.
--
-- Database names arrive as sqlcmd variables ($(DB_ORDERS) etc.), set from the
-- container environment by that entrypoint, so they stay a single source of
-- truth with .env — the same reasoning that made #7 use a .sh rather than a
-- .sql there. sqlcmd substitution is textual and happens before the batch is
-- parsed, so [$(DB_ORDERS)] is a legal identifier reference.
--
-- IDEMPOTENT BY CONSTRUCTION. Unlike MySQL's initdb hook, which runs once
-- against an empty data directory, this script re-runs on every container
-- start. Every statement is therefore guarded — the second run must be a
-- no-op, not an error.

SET NOCOUNT ON;
GO

-- ── databases ───────────────────────────────────────────────────────────
IF DB_ID(N'$(DB_ORDERS)')        IS NULL CREATE DATABASE [$(DB_ORDERS)];
GO
IF DB_ID(N'$(DB_FULFILLMENT)')   IS NULL CREATE DATABASE [$(DB_FULFILLMENT)];
GO
IF DB_ID(N'$(DB_BILLING)')       IS NULL CREATE DATABASE [$(DB_BILLING)];
GO
IF DB_ID(N'$(DB_NOTIFICATIONS)') IS NULL CREATE DATABASE [$(DB_NOTIFICATIONS)];
GO

-- No n8n database here, deliberately. n8n does not support MS-SQL as its own
-- store at all (its DB_TYPE accepts sqlite/postgresdb only), so it runs on its
-- default SQLite volume in this compose file. #7 reached the same end state by
-- a different route: it created an `n8n` MySQL database that n8n never once
-- connected to, and removed it in review fix D4. Not creating it in the first
-- place is that lesson applied rather than repeated.

-- ── application login ───────────────────────────────────────────────────
-- One server-level login, one database-level user per database. The login is
-- server-scoped in MS-SQL, unlike MySQL where GRANT alone does both jobs.
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(APP_USER)')
BEGIN
    CREATE LOGIN [$(APP_USER)]
        WITH PASSWORD = N'$(APP_PASSWORD)',
             CHECK_POLICY = OFF,      -- dev container; the password lives in .env
             DEFAULT_DATABASE = [$(DB_ORDERS)];
END
GO

-- ── per-database user + db_owner ────────────────────────────────────────
-- db_owner rather than a narrower role because EF Core migrations create,
-- alter and drop objects in these databases; a least-privilege split between
-- a migration principal and a runtime principal is a production concern
-- documented in the README, not something a dev compose file should fake.
USE [$(DB_ORDERS)];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(APP_USER)')
    CREATE USER [$(APP_USER)] FOR LOGIN [$(APP_USER)];
ALTER ROLE db_owner ADD MEMBER [$(APP_USER)];
GO

USE [$(DB_FULFILLMENT)];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(APP_USER)')
    CREATE USER [$(APP_USER)] FOR LOGIN [$(APP_USER)];
ALTER ROLE db_owner ADD MEMBER [$(APP_USER)];
GO

USE [$(DB_BILLING)];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(APP_USER)')
    CREATE USER [$(APP_USER)] FOR LOGIN [$(APP_USER)];
ALTER ROLE db_owner ADD MEMBER [$(APP_USER)];
GO

USE [$(DB_NOTIFICATIONS)];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(APP_USER)')
    CREATE USER [$(APP_USER)] FOR LOGIN [$(APP_USER)];
ALTER ROLE db_owner ADD MEMBER [$(APP_USER)];
GO

USE [master];
GO

PRINT 'otc bootstrap: four databases and the application login are present.';
GO
