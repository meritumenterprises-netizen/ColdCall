IF OBJECT_ID(N'ColdCall.CompanyUserNotes', N'U') IS NULL
BEGIN
    CREATE TABLE [ColdCall].[CompanyUserNotes]
    (
        [ID] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CompanyUserNotes] PRIMARY KEY,
        [CompanyID] int NOT NULL,
        [UserId] int NOT NULL,
        [Notes] nvarchar(max) NOT NULL,
        CONSTRAINT [FK_CompanyUserNotes_Companies] FOREIGN KEY ([CompanyID]) REFERENCES [ColdCall].[Companies] ([Id]),
        CONSTRAINT [FK_CompanyUserNotes_Users] FOREIGN KEY ([UserId]) REFERENCES [ColdCall].[Users] ([Id])
    );
    CREATE UNIQUE INDEX [UX_CompanyUserNotes_CompanyID_UserId]
        ON [ColdCall].[CompanyUserNotes] ([CompanyID], [UserId]);
END;
