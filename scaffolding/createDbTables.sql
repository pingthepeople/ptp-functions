DROP table [Session]
Drop Table [Committee]

DROP TABLE [UserBill]
DROP TABLE [BillCommittee]
DROP TABLE [Action]
DROP TABLE [Schedule]
Drop TABLE [Bill]
drop table [User]

CREATE TABLE [Session]
(
    Id int IDENTITY(1,1) PRIMARY KEY,
    Name nvarchar(256) NOT NULL,
    Link nvarchar(256) NOT NULL
)

CREATE TABLE Committee
(
    Id int IDENTITY(1,1) PRIMARY KEY,
    Name nvarchar(256) NOT NULL,
    Link nvarchar(256) NOT NULL,
    Chamber TINYINT NOT NULL,
    SessionId int NOT NULL FOREIGN KEY REFERENCES [Session](Id)
)

CREATE TABLE [User]
(
    Id int IDENTITY(1,1) PRIMARY KEY,
    Name nvarchar(256),
    Email nvarchar(256) NOT NULL,
    Mobile nvarchar(256),
    ReceiveDigestEmail bit NOT NULL DEFAULT 1
)

CREATE TABLE Bill
(
    Id int IDENTITY(1,1) PRIMARY KEY,
    Name nvarchar(256) NOT NULL,
    Link nvarchar(256) NOT NULL,
    Title nvarchar(256) NOT NULL,
    Description nvarchar(max),
    Topics nvarchar(max),
    Authors nvarchar(256),
    SessionId int NOT NULL FOREIGN KEY REFERENCES [Session](Id)
)


CREATE TABLE [Action]
(
    Id int IDENTITY(1,1) PRIMARY KEY,
    Link nvarchar(256) NOT NULL,
    Description nvarchar(256) NOT NULL,
    Date DATETIME NOT NULL,
    Chamber TINYINT NOT NULL,
    ActionType TINYINT NOT NULL,
    BillId int NOT NULL FOREIGN KEY REFERENCES Bill(Id),
)

Create Table Schedule
(
    Id int IDENTITY(1,1) PRIMARY KEY,
    OriginCommitteeReading Date,
    OriginSecondReading Date,
    OriginThirdReading Date,
    CrossoverCommitteeReading Date,
    CrossoverSecondReading Date,
    CrossoverThirdReading Date,
    BillId int NOT NULL FOREIGN KEY REFERENCES Bill(Id),
)

-- Many-to-many tables

Create Table UserBill
(
    Id int IDENTITY(1,1) PRIMARY KEY,
    ReceiveAlertEmail bit NOT NULL DEFAULT 0,
    ReceiveAlertSms bit NOT NULL DEFAULT 0,
    BillId int NOT NULL FOREIGN KEY REFERENCES Bill(Id),
    UserId int NOT NULL FOREIGN KEY REFERENCES [User](Id)
)

CREATE TABLE BillCommittee
(
    Id int IDENTITY(1,1) PRIMARY KEY,
    Assigned DATETIME,
    BillId int FOREIGN KEY REFERENCES Bill(Id),
    CommitteeId int FOREIGN KEY REFERENCES Committee(Id),
)
