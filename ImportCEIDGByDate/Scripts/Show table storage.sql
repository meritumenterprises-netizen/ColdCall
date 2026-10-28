USE [Firmy_temp];

SELECT
    s.name AS SchemaName,
    t.name AS TableName,
    SUM(CASE WHEN p.index_id IN (0, 1)
             THEN p.row_count ELSE 0 END) AS [RowCount],
    CAST(SUM(p.reserved_page_count) / 128.0 AS decimal(18, 2)) AS AllocatedMB,
    CAST(SUM(p.used_page_count) / 128.0 AS decimal(18, 2)) AS UsedMB,
    CAST(SUM(p.reserved_page_count - p.used_page_count)
         / 128.0 AS decimal(18, 2)) AS UnusedMB
FROM sys.tables AS t
JOIN sys.schemas AS s
    ON s.schema_id = t.schema_id
JOIN sys.dm_db_partition_stats AS p
    ON p.object_id = t.object_id
GROUP BY s.name, t.name
ORDER BY AllocatedMB DESC;