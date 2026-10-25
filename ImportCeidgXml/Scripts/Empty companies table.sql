-- takes 6 minutes 20 seconds

BEGIN TRY
    BEGIN TRANSACTION;

    TRUNCATE TABLE ColdCall.CompanyPkds;
    TRUNCATE TABLE ColdCall.CompanyAddress;
    TRUNCATE TABLE ColdCall.CompanyUserNotes;
    TRUNCATE TABLE ColdCall.CompanyXml;
    TRUNCATE TABLE ColdCall.Adresy
    TRUNCATE TABLE ColdCall.Companies;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;