USE master;
GO

IF DB_ID(N'Firmy_temp') IS NOT NULL
    THROW 50001, 'Database Firmy_temp already exists.', 1;

CREATE DATABASE [Firmy_temp]
ON
    (FILENAME = N'C:\SQLData\Firmy_temp.mdf'),
    (FILENAME = N'C:\SQLData\Firmy_temp_log.ldf')
FOR ATTACH;
GO