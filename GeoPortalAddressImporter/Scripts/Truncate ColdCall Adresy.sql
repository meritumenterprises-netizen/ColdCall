BEGIN TRY
    BEGIN TRANSACTION;

    TRUNCATE [ColdCall].[CompanyAddress];

    ALTER TABLE [ColdCall].[CompanyAddress]
        DROP CONSTRAINT [FK_CompanyAddress_Adresy];

    TRUNCATE TABLE [ColdCall].[Adresy];

    ALTER TABLE [ColdCall].[CompanyAddress] WITH CHECK
        ADD CONSTRAINT [FK_CompanyAddress_Adresy]
        FOREIGN KEY ([AddressId])
        REFERENCES [ColdCall].[Adresy] ([Id]);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;