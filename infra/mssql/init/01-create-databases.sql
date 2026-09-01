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

-- ── isolation level ─────────────────────────────────────────────────────
-- READ_COMMITTED_SNAPSHOT ON, and the reason is parity rather than taste.
--
-- #7 ran on MySQL/InnoDB, whose default REPEATABLE READ serves consistent
-- reads from MVCC without taking read locks: a reader never blocks on a
-- writer. MS-SQL's default READ COMMITTED is lock-based, so the same code
-- would block where #7 did not — a behavioural divergence invisible in tests
-- and visible only under concurrent load, which is the worst way to find one.
-- RCSI makes READ COMMITTED use row versioning, which is the closest MS-SQL
-- gets to the semantics the shared spec was written against.
--
-- It does NOT weaken the two places that matter, because both take explicit
-- locks rather than relying on the ambient level: the outbox relay claims
-- rows WITH (UPDLOCK, READPAST, ROWLOCK), and the stock reservation path
-- takes WITH (UPDLOCK, HOLDLOCK) in a fixed order. Those behave identically
-- under either setting.
--
-- Cost: a version store in tempdb and ~14 bytes per row. Set at creation
-- time because retrofitting it needs exclusive database access, which is
-- cheap now and disruptive once six services hold pools open.
--
-- WITH ROLLBACK IMMEDIATE so the statement cannot hang waiting for a session
-- to disconnect; on a fresh dev container there is nothing to roll back.
DECLARE @db sysname, @sql nvarchar(max);
DECLARE db_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT name FROM sys.databases
    WHERE name IN (N'$(DB_ORDERS)', N'$(DB_FULFILLMENT)', N'$(DB_BILLING)', N'$(DB_NOTIFICATIONS)')
      AND is_read_committed_snapshot_on = 0;
OPEN db_cursor;
FETCH NEXT FROM db_cursor INTO @db;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'ALTER DATABASE ' + QUOTENAME(@db)
             + N' SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;';
    EXEC sp_executesql @sql;
    FETCH NEXT FROM db_cursor INTO @db;
END
CLOSE db_cursor;
DEALLOCATE db_cursor;
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
