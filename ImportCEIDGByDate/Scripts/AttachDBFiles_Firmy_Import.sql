USE master;
GO

CREATE DATABASE [Firmy_Import]
ON
(
    FILENAME = N'C:\SQLData\Firmy_Import.mdf'
),
(
    FILENAME = N'C:\SQLData\Firmy_Import.ldf'
)
FOR ATTACH;
GO