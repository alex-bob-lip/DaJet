/****** Script for SelectTopNRows command from SSMS  ******/
SELECT
	SUBSTRING([_Fld104], 1, 13) AS [������������������������],
	CAST(SUBSTRING([_Fld104], 14, DATALENGTH([_Fld104])) AS varchar(max)) AS [�����������������]
FROM
	[my_exchange].[dbo].[_Const103]
