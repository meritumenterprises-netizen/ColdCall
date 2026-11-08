SELECT
    name AS LogicalName,
    type_desc AS FileType,
    physical_name AS FilePath
FROM sys.master_files
WHERE database_id = DB_ID(N'Firmy_temp');