IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [RechargeTransactions] (
    [Id] int NOT NULL IDENTITY,
    [TransactionId] AS 'TXN-' + CAST([Id] + 1000 AS nvarchar(20)) PERSISTED,
    [MobileNumber] nvarchar(10) NOT NULL,
    [Amount] decimal(10,2) NOT NULL,
    [Status] nvarchar(10) NOT NULL,
    [PaymentReference] nvarchar(30) NULL,
    [FailureReason] nvarchar(200) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_RechargeTransactions] PRIMARY KEY ([Id])
);

CREATE INDEX [IX_RechargeTransactions_MobileNumber_CreatedAt] ON [RechargeTransactions] ([MobileNumber], [CreatedAt]);

CREATE UNIQUE INDEX [IX_RechargeTransactions_TransactionId] ON [RechargeTransactions] ([TransactionId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261108091551_InitialCreate', N'10.0.14');

COMMIT;
GO

