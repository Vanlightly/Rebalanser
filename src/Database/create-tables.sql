CREATE SCHEMA RBR;

CREATE TABLE [RBR].[ResourceGroups](
	[ResourceGroup] [varchar](100) NOT NULL,
	[CoordinatorId] [uniqueidentifier] NULL,
	[LastCoordinatorRenewal] [datetime] NULL,
	[CoordinatorServer] [nchar](500) NULL,
	[LockedByClient] [uniqueidentifier] NULL,
	[FencingToken] [int] NOT NULL,
	[LeaseExpirySeconds] [int] NOT NULL,
 CONSTRAINT [ResourceGroupsPK] PRIMARY KEY CLUSTERED 
(
	[ResourceGroup] ASC
));

CREATE TABLE [RBR].[Clients](
	[ClientId] [uniqueidentifier] NOT NULL,
	[ResourceGroup] [varchar](100) NOT NULL,
	[LastKeepAlive] [datetime] NOT NULL,
	[ClientStatus] [tinyint] NOT NULL,
	[CoordinatorStatus] [tinyint] NOT NULL,
	[Resources] [varchar](max) NOT NULL,
	[FencingToken] [int] NOT NULL,
 CONSTRAINT [ConsumersPK] PRIMARY KEY CLUSTERED 
(
	[ClientId] ASC
));

ALTER TABLE [RBR].[Clients] ADD  DEFAULT ((1)) FOR [FencingToken];

ALTER TABLE [RBR].[Clients]  WITH CHECK ADD  CONSTRAINT [FK_Clients_ResourceGroups] FOREIGN KEY([ResourceGroup])
REFERENCES [RBR].[ResourceGroups] ([ResourceGroup]);

ALTER TABLE [RBR].[Clients] CHECK CONSTRAINT [FK_Clients_ResourceGroups];

CREATE TABLE [RBR].[Resources](
	[ResourceGroup] [varchar](100) NOT NULL,
	[ResourceName] [varchar](100) NOT NULL,
 CONSTRAINT [QueuesPK] PRIMARY KEY CLUSTERED 
(
	[ResourceGroup] ASC,
	[ResourceName] ASC
));

ALTER TABLE [RBR].[Resources]  WITH CHECK ADD  CONSTRAINT [FK_Resources_ResourceGroups] FOREIGN KEY([ResourceGroup])
REFERENCES [RBR].[ResourceGroups] ([ResourceGroup]);

ALTER TABLE [RBR].[Resources] CHECK CONSTRAINT [FK_Resources_ResourceGroups];