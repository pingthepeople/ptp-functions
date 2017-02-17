CREATE TABLE Users
(
    Id int NOT NULL PRIMARY KEY,
    Name nvarchar(256),
    Email nvarchar(256) NOT NULL,
    Mobile nvarchar(256),
    ReceiveDigestEmail bit NOT NULL DEFAULT 1
)

CREATE TABLE [Sessions]
(
    Id int NOT NULL PRIMARY KEY,
    Name nvarchar(256) NOT NULL,
    Chamber TINYINT NOT NULL
)

CREATE TABLE Committees
(
    Id int NOT NULL PRIMARY KEY,
    Name nvarchar(256) NOT NULL,
    Link nvarchar(256) NOT NULL,
    Chamber TINYINT NOT NULL,
    SessionId int NOT NULL FOREIGN KEY REFERENCES Sessions(Id)
)

CREATE TABLE Bills
(
    Id int NOT NULL PRIMARY KEY,
    Name nvarchar(256) NOT NULL,
    Link nvarchar(256) NOT NULL,
    Title nvarchar(256) NOT NULL,
    Description nvarchar(max),
    Topics nvarchar(max),
    Authors nvarchar(256),
    SessionId int NOT NULL FOREIGN KEY REFERENCES Sessions(Id),
    HouseCommitteeId int FOREIGN KEY REFERENCES Committees(Id),
    SenateCommitteeId int FOREIGN KEY REFERENCES Committees(Id),
)

CREATE TABLE Actions
(
    Id int NOT NULL PRIMARY KEY,
    Link nvarchar(256) NOT NULL,
    Description nvarchar(256) NOT NULL,
    Date DATETIME NOT NULL,
    Chamber TINYINT NOT NULL,
    BillId int NOT NULL FOREIGN KEY REFERENCES Bills(Id),
)

Create Table Schedules
(
    Id int NOT NULL PRIMARY KEY,
    OriginCommitteeReading Date,
    OriginSecondReading Date,
    OriginThirdReading Date,
    CrossoverCommitteeReading Date,
    CrossoverSecondReading Date,
    CrossoverThirdReading Date,
    BillId int NOT NULL FOREIGN KEY REFERENCES Bills(Id),
)

Create Table UserBills
(
    Id int NOT NULL PRIMARY KEY,
    ReceiveAlertEmail bit NOT NULL DEFAULT 0,
    ReceiveAlertSms bit NOT NULL DEFAULT 0,
    BillId int NOT NULL FOREIGN KEY REFERENCES Bills(Id),
    UserId int NOT NULL FOREIGN KEY REFERENCES Users(Id)
)