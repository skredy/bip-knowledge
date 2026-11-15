CREATE TABLE dbo.Commande (
    Id int NOT NULL PRIMARY KEY,
    ClientId int NOT NULL,
    Statut nvarchar(30) NOT NULL
);
