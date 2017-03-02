CREATE TABLE [Session]
(
    Id int IDENTITY(1,1) PRIMARY KEY,
    Name nvarchar(256) NOT NULL,
    Link nvarchar(256) NOT NULL,
    Created DATETIME NOT NULL DEFAULT GetUtcDate()
)

CREATE TABLE Committee
(
    Id int IDENTITY(1,1) PRIMARY KEY,
    Name nvarchar(256) NOT NULL,
    Link nvarchar(256) NOT NULL,
    Chamber TINYINT NOT NULL,
    Created DATETIME NOT NULL DEFAULT GetUtcDate(),
    SessionId int NOT NULL FOREIGN KEY REFERENCES [Session](Id)
)

CREATE TABLE Subject
(
    Id int IDENTITY(1,1) PRIMARY KEY,
    Name nvarchar(256) NOT NULL,
    Link nvarchar(256) NOT NULL,
    Created DATETIME NOT NULL DEFAULT GetUtcDate(),
    SessionId int NOT NULL FOREIGN KEY REFERENCES [Session](Id)
)

CREATE TABLE [Users]
(
    Id int IDENTITY(1,1) PRIMARY KEY,
    Name nvarchar(256),
    Email nvarchar(256) NOT NULL,
    Mobile nvarchar(256),
    DigestType TINYINT NOT NULL DEFAULT 0,
    Created DATETIME NOT NULL DEFAULT GetUtcDate()
)

CREATE TABLE Bill
(
    Id int IDENTITY(1,1) PRIMARY KEY,
    Name nvarchar(256) NOT NULL,
    Link nvarchar(256) NOT NULL,
    Title nvarchar(256) NOT NULL,
    Description nvarchar(max),
    Authors nvarchar(256),
    Chamber TINYINT NOT NULL,
    Created DATETIME NOT NULL DEFAULT GetUtcDate(),
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
    Created DATETIME NOT NULL DEFAULT GetUtcDate(),
    BillId int NOT NULL FOREIGN KEY REFERENCES Bill(Id),
)

CREATE TABLE ScheduledAction
(
    Id int IDENTITY(1,1) PRIMARY KEY,
    Link nvarchar(256) NOT NULL,
    Chamber TINYINT NOT NULL,
    ActionType TINYINT NOT NULL,
    Date DATETIME NOT NULL,
    [Start] nvarchar(16) NOT NULL,
    [End] nvarchar(16) NOT NULL,
    Location nvarchar(256) NOT NULL,
    Created DATETIME NOT NULL DEFAULT GetUtcDate(),
    BillId int NOT NULL FOREIGN KEY REFERENCES Bill(Id),
)

-- Many-to-many tables

Create Table UserBill
(
    Id int IDENTITY(1,1) PRIMARY KEY,
    ReceiveAlertEmail bit NOT NULL DEFAULT 0,
    ReceiveAlertSms bit NOT NULL DEFAULT 0,
    Created DATETIME NOT NULL DEFAULT GetUtcDate(),
    BillId int NOT NULL FOREIGN KEY REFERENCES Bill(Id),
    UserId int NOT NULL FOREIGN KEY REFERENCES [User](Id)
)

CREATE TABLE BillCommittee
(
    Id int IDENTITY(1,1) PRIMARY KEY,
    Assigned DATETIME,
    Created DATETIME NOT NULL DEFAULT GetUtcDate(),
    BillId int FOREIGN KEY REFERENCES Bill(Id),
    CommitteeId int FOREIGN KEY REFERENCES Committee(Id),
)

CREATE TABLE BillSubject
(
    Id int IDENTITY(1,1) PRIMARY KEY,
    Assigned DATETIME,
    Created DATETIME NOT NULL DEFAULT GetUtcDate(),
    BillId int FOREIGN KEY REFERENCES Bill(Id),
    SubjectId int FOREIGN KEY REFERENCES [Subject](Id),
)

-- Laravel Migrations table

CREATE TABLE migrations (
  id INT IDENTITY(1,1) PRIMARY KEY,
  migration varchar(255) NOT NULL,
  batch INT NOT NULL,
) 