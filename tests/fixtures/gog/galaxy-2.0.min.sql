-- Winnow fixture: minimal GOG Galaxy 2.0 client database.
-- Generated from a live galaxy-2.0.db (Galaxy 2.1.8.30, schema user_version=40) and SANITIZED.
-- Real CREATE TABLE text preserved verbatim so constraints/casing match production.
-- Regenerate galaxy-2.0.min.db with:  sqlite3 galaxy-2.0.min.db < galaxy-2.0.min.sql
PRAGMA foreign_keys=OFF;
BEGIN TRANSACTION;
PRAGMA user_version=40;

CREATE TABLE Users(
	'id' INT64 NOT NULL PRIMARY KEY
);
INSERT INTO "Users" ("id") VALUES (11111111111111111);

CREATE TABLE 'Platforms' (
	'id' INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
	'name' TEXT NOT NULL,
	CONSTRAINT 'UK_Platforms_name'
		UNIQUE ('name'),
	CONSTRAINT 'CK_Platforms_name'
		CHECK(trim(name) <> '')
);
INSERT INTO "Platforms" ("id","name") VALUES (3,'epic');
INSERT INTO "Platforms" ("id","name") VALUES (5,'steam');
INSERT INTO "Platforms" ("id","name") VALUES (69,'winstore');
INSERT INTO "Platforms" ("id","name") VALUES (85,'rockstar');

CREATE TABLE 'PlatformConnections'(
	'userId' INT64 NOT NULL,
	'platform' TEXT NOT NULL,
	'connectionState' TEXT NOT NULL,
	CONSTRAINT 'FK_PlatformConnections_userId_Users_id'
		FOREIGN KEY ('userId')
		REFERENCES 'Users' ('id') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'FK_PlatformConnections_platform_Platforms_name'
		FOREIGN KEY ('platform')
		REFERENCES 'Platforms' ('name') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'UK_PlatformConnections_userId_platform'
		UNIQUE ('userId', 'platform')
);
INSERT INTO "PlatformConnections" ("userId","platform","connectionState") VALUES (11111111111111111,'epic','Disconnected');
INSERT INTO "PlatformConnections" ("userId","platform","connectionState") VALUES (11111111111111111,'steam','Disconnected');
INSERT INTO "PlatformConnections" ("userId","platform","connectionState") VALUES (11111111111111111,'winstore','Disconnected');
INSERT INTO "PlatformConnections" ("userId","platform","connectionState") VALUES (11111111111111111,'rockstar','Disconnected');

CREATE TABLE 'ReleaseKeys'(
	'key' TEXT NOT NULL PRIMARY KEY,
	CONSTRAINT 'CK_ReleaseKeys_key'
		CHECK(trim([key]) <> '' AND key LIKE '_%\_%_' ESCAPE '\')
);
INSERT INTO "ReleaseKeys" ("key") VALUES ('gog_1971477531');
INSERT INTO "ReleaseKeys" ("key") VALUES ('gog_1207664643');
INSERT INTO "ReleaseKeys" ("key") VALUES ('gog_1430742983');
INSERT INTO "ReleaseKeys" ("key") VALUES ('gog_1207658901');
INSERT INTO "ReleaseKeys" ("key") VALUES ('steam_1091500');
INSERT INTO "ReleaseKeys" ("key") VALUES ('gog_2074191081');

CREATE TABLE 'LibraryReleases'(
	'id' INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
	'userId' INT64 NOT NULL,
	'releaseKey' TEXT NOT NULL,
	CONSTRAINT 'FK_LibraryReleases_releaseKey_ReleaseKeys_key'
		FOREIGN KEY ('releaseKey')
		REFERENCES 'ReleaseKeys' ('key') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'FK_LibraryReleases_userId_Users_id'
		FOREIGN KEY ('userId')
		REFERENCES 'Users' ('id') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'UK_LibraryReleases_userId_key'
		UNIQUE ('userId', 'releaseKey')
);
INSERT INTO "LibraryReleases" ("id","userId","releaseKey") VALUES (4,11111111111111111,'gog_1430742983');
INSERT INTO "LibraryReleases" ("id","userId","releaseKey") VALUES (6,11111111111111111,'steam_1091500');
INSERT INTO "LibraryReleases" ("id","userId","releaseKey") VALUES (10,11111111111111111,'gog_1207658901');
INSERT INTO "LibraryReleases" ("id","userId","releaseKey") VALUES (35,11111111111111111,'gog_1207664643');
INSERT INTO "LibraryReleases" ("id","userId","releaseKey") VALUES (43,11111111111111111,'gog_1971477531');

CREATE TABLE 'LicensedReleases'(
	'libraryId' INTEGER NOT NULL,
	'isOwned' BOOLEAN NOT NULL,
	CONSTRAINT 'FK_LicensedReleases_libraryId_LibraryReleases_id'
		FOREIGN KEY ('libraryId')
		REFERENCES 'LibraryReleases' ('id') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'UK_LicensedReleases_libraryId'
		UNIQUE ('libraryId')
);
INSERT INTO "LicensedReleases" ("libraryId","isOwned") VALUES (4,1);
INSERT INTO "LicensedReleases" ("libraryId","isOwned") VALUES (6,1);
INSERT INTO "LicensedReleases" ("libraryId","isOwned") VALUES (10,1);
INSERT INTO "LicensedReleases" ("libraryId","isOwned") VALUES (35,1);
INSERT INTO "LicensedReleases" ("libraryId","isOwned") VALUES (43,1);

CREATE TABLE 'ReleaseProperties' (
	'releaseKey' TEXT NOT NULL,
	'isDlc' INTEGER NULL DEFAULT NULL,
	'isVisibleInLibrary' INTEGER NULL DEFAULT NULL,
	'gameId' TEXT NULL DEFAULT NULL,
	CONSTRAINT 'FK_ReleaseProperties_releaseKey_ReleaseKeys_key'
		FOREIGN KEY ('releaseKey')
		REFERENCES 'ReleaseKeys' ('key') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'UK_ReleaseProperties_gameReleaseKey'
		UNIQUE ('releaseKey')
);
INSERT INTO "ReleaseProperties" ("releaseKey","isDlc","isVisibleInLibrary","gameId") VALUES ('gog_1207658901',0,1,'51295416355398085');
INSERT INTO "ReleaseProperties" ("releaseKey","isDlc","isVisibleInLibrary","gameId") VALUES ('gog_1207664643',0,1,'51071842242777057');
INSERT INTO "ReleaseProperties" ("releaseKey","isDlc","isVisibleInLibrary","gameId") VALUES ('gog_1430742983',1,1,'52051794812828355');
INSERT INTO "ReleaseProperties" ("releaseKey","isDlc","isVisibleInLibrary","gameId") VALUES ('gog_1971477531',0,1,'51152944180514264');
INSERT INTO "ReleaseProperties" ("releaseKey","isDlc","isVisibleInLibrary","gameId") VALUES ('gog_2074191081',0,1,'51152944180514264');
INSERT INTO "ReleaseProperties" ("releaseKey","isDlc","isVisibleInLibrary","gameId") VALUES ('steam_1091500',0,1,'51152725611062641');

CREATE TABLE 'UserReleaseProperties' (
	'userId' INT64 NOT NULL,
	'releaseKey' TEXT NOT NULL,
	'isHidden' BOOLEAN NULL,
	'hasTagsFetched' BOOLEAN NOT NULL DEFAULT 0,
	CONSTRAINT 'FK_UserReleaseProperties_userId_Users_id'
		FOREIGN KEY ('userId')
		REFERENCES 'Users' ('id') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'FK_UserReleaseProperties_releaseKey_ReleaseKeys_key'
		FOREIGN KEY ('releaseKey')
		REFERENCES 'ReleaseKeys' ('key') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'UK_UserReleaseProperties_userId_releaseKey'
		UNIQUE ('userId', 'releaseKey')
);
INSERT INTO "UserReleaseProperties" ("userId","releaseKey","isHidden","hasTagsFetched") VALUES (11111111111111111,'gog_1430742983',0,1);
INSERT INTO "UserReleaseProperties" ("userId","releaseKey","isHidden","hasTagsFetched") VALUES (11111111111111111,'steam_1091500',0,1);
INSERT INTO "UserReleaseProperties" ("userId","releaseKey","isHidden","hasTagsFetched") VALUES (11111111111111111,'gog_1971477531',0,1);
INSERT INTO "UserReleaseProperties" ("userId","releaseKey","isHidden","hasTagsFetched") VALUES (11111111111111111,'gog_1207658901',0,1);
INSERT INTO "UserReleaseProperties" ("userId","releaseKey","isHidden","hasTagsFetched") VALUES (11111111111111111,'gog_1207664643',0,1);

CREATE TABLE 'GamePieceTypes'(
	'id' INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
	'type' TEXT NOT NULL,
	CONSTRAINT 'UK_GamePieceTypes_type'
		UNIQUE ('type'),
	CONSTRAINT 'CK_GamePieceTypes_type'
		CHECK(trim(type) <> '')
);
INSERT INTO "GamePieceTypes" ("id","type") VALUES (47,'allGameReleases');
INSERT INTO "GamePieceTypes" ("id","type") VALUES (48,'dlcs');
INSERT INTO "GamePieceTypes" ("id","type") VALUES (91,'meta');
INSERT INTO "GamePieceTypes" ("id","type") VALUES (53,'originalTitle');
INSERT INTO "GamePieceTypes" ("id","type") VALUES (54,'osCompatibility');
INSERT INTO "GamePieceTypes" ("id","type") VALUES (97,'sortingTitle');
INSERT INTO "GamePieceTypes" ("id","type") VALUES (190,'storeTags');
INSERT INTO "GamePieceTypes" ("id","type") VALUES (99,'title');

CREATE TABLE 'GamePieces'(
	'releaseKey' TEXT NOT NULL,
	'gamePieceTypeId' INTEGER NOT NULL,
	'userId' INT64,
	'value' TEXT NOT NULL,
	'languageId' INTEGER NULL,
	CONSTRAINT 'FK_GamePieces_releaseKey_ReleaseKeys_key'
		FOREIGN KEY ('releaseKey')
		REFERENCES 'ReleaseKeys' ('key') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'FK_GamePieces_gamePieceTypeId_GamePieceTypes_id'
		FOREIGN KEY ('gamePieceTypeId')
		REFERENCES 'GamePieceTypes' ('id') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'FK_GamePieces_userId_Users_id'
		FOREIGN KEY ('userId')
		REFERENCES 'Users' ('id') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'FK_GamePieces_languageId_RecentClientLanguages_languageId'
		FOREIGN KEY ('languageId')
		REFERENCES 'RecentClientLanguages' ('languageId') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'UK_GamePieces_releaseKey_gamePieceTypeId_userId_languageId'
		UNIQUE ('releaseKey', 'gamePieceTypeId', 'userId', 'languageId'),
	CONSTRAINT 'CK_GamePieces_value'
		CHECK(trim([value]) <> ''),
	CONSTRAINT `CK_GamePieces_userId`
		CHECK([userId] > 0 OR [userId] IS NULL)
);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1207658901',47,NULL,'{"releases":["gog_1207658901","ngameboy_6784","ngameboy_Tyrian 2000","ngameboy_3303251459","jaguar_25805655","test_25805655","generic_51295416355398085"]}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1207658901',48,NULL,'{"dlcs":[]}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1207658901',53,11111111111111111,'{"title":"Tyrian 2000"}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1207658901',54,NULL,'{"supported":[{"name":"Windows","slug":"windows","versions":null},{"name":"macOS","slug":"osx","versions":null}]}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1207658901',91,11111111111111111,'{"criticsScore":null,"developers":["Eclipse Software"],"genres":["Shooter","Arcade"],"publishers":["XSIV Games"],"releaseDate":943920000,"themes":["Action","Science fiction"]}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1207658901',97,11111111111111111,'{"isModifiedByUser":false,"title":"Tyrian 2000"}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1207658901',99,11111111111111111,'{"title":"Tyrian 2000"}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1207664643',47,NULL,'{"releases":["steam_292030","xboxone_1799887933","xboxone_1111111222","gog_1207664643","gog_1425895904","gog_1428055479","origin_Origin.OFR.50.0001017","origin_Origin.OFR.50.0001016","origin_Origin.OFR.50.0000743","psn_CUSA01440_00","psn_CUSA01439_00","origin_5","battlenet_5","psn_CUSA00527_00","psn_CUSA01441_00","psn_CUSA01470_00","psn_CUSA01490_00","gg_292030","origin_Origin.OFR.50.0000744","test_CUSA00527_00","humble_thewitcher3_wildhunt_gog","nswitch_0100BFE00E9CA000","itch_e24b6f654169e2e8d9622deb5840a9f5b74668f31a21211104f00524","itch_e1b518d66bfc6c08ffc4c4dd0cc056dd2359fa5b93eb954b86160ad9","origin_6fa071681b97926e99e8584f490b30ab585d02fe","origin_c2fb6d80f6b21168e3cd12c86bee994daa7f9a2c","origin_e480ceaedb10cd8a6a789442f22def9b03d2abc1180619b7eed877d88cf280e0","origin_4bf539aaa95923322fcbe63619d8a9b2e6f977f365134e05236cd16657f2bce8","origin_d1b5e504cb1771f0cc7564b6146d6b3a2625fd3cb0ef1cc7cb37080f05ee5369","origin_f13a4b8087602c0d2746d0ff1f0ac259a2628bef","nswitch_0100A0800E9C4000","epic_14ee004dadc142faaaece5a6270fb628","origin_1207664663","origin_292030","epic_292030","test_292030","test_499450","steam_420699","psx_CUSA01439_00","steam_steam_292030","origin_42690","test_/the-witcher-3-wild-hunt-free-pc-download/","test_the-witcher-3-wild-hunt","playfire_the-witcher-3-wild-hunt","humble_thewitcher3_wildhunt_gog_keyless","origin_Origin.OFR.50.0000843","origin_Origin.OFR.50.0001672@gifting","nswitch_01003D100E9C6000","gog_1425982292","nswitch_0100E67012924000","test_gfn_292030","test_CUSA01439_00","humble_thewitcher3_wildhunt_us_switch","psn_PPSA10408_00","psn_PPSA10412_00","psn_PPSA10407_00","psn_PPSA10409_00","psn_PPSA10410_00","test_gfn_100044011","test_gog_1207664643","test_steam_292030","test_100044011","psn_PPSA04025_00","test_56990d74b3cbf1eb918703be67afd59792308952","generic_51071842242777057"]}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1207664643',48,NULL,'{"dlcs":["gog_1441355562","gog_1441620909","gog_2030271920","gog_1430743168","gog_1430742983","gog_1430743218","gog_1430743030","gog_1430742787","gog_1430742762","gog_1430743139","gog_1430742867","gog_1430743081","gog_1430742866","gog_1430742926","gog_1430743108","gog_1430742662","gog_1430743167","gog_1430742826","gog_1430742899","gog_1430743056","gog_1430742953","gog_1441618827","gog_1441620485","gog_1145229461"]}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1207664643',53,11111111111111111,'{"title":"The Witcher 3: Wild Hunt - Complete Edition"}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1207664643',54,NULL,'{"supported":[{"name":"Windows","slug":"windows","versions":null}]}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1207664643',91,11111111111111111,'{"criticsScore":91.7308,"developers":["CD Projekt RED"],"genres":["Role-playing (RPG)","Adventure"],"publishers":["Bandai Namco Entertainment","cdp.pl","WB Games","Spike Chunsoft"],"releaseDate":1431993600,"themes":["Open world","Action","Fantasy"]}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1207664643',97,11111111111111111,'{"isModifiedByUser":false,"title":"Witcher 3 Wild Hunt Complete Edition"}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1207664643',99,11111111111111111,'{"title":"The Witcher 3: Wild Hunt - Complete Edition"}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1430742983',47,NULL,'{"releases":["gog_1430742983","generic_52051794812828355"]}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1430742983',48,NULL,'{"dlcs":[]}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1430742983',53,11111111111111111,'{"title":"New Game +"}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1430742983',54,NULL,'{"supported":[{"name":"Windows","slug":"windows","versions":null}]}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1430742983',91,11111111111111111,'{"criticsScore":null,"developers":["TEST DEVELOPER 2"],"genres":[],"publishers":["TEST PUBLISHER 2"],"releaseDate":1431993600,"themes":[]}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1430742983',97,11111111111111111,'{"isModifiedByUser":false,"title":"New Game "}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1430742983',99,11111111111111111,'{"title":"New Game +"}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1971477531',47,NULL,'{"releases":["gog_1971477531","xboxone_606152324","psn_CUSA08234_00","psn_CUSA08213_00","xboxone_1671755991","psn_CUSA08159_00","test_CUSA08213_00","egg_gwent","steam_1284410","test_gfn_1284410","test_1284410","test_gog_1971477531","test_gfn_18541411","test_steam_1284410","test_18541411","gog_2074191081","generic_51152944180514264"]}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1971477531',48,NULL,'{"dlcs":["gog_1142753074","gog_1286889002","gog_1375074776","gog_1502999678","gog_1768423553","gog_1637302687","gog_1704610657","gog_1499007217","gog_1261210204","gog_1826735629","gog_1241199566","gog_1588347234","gog_1905569932","gog_1270385236","gog_1147665426","gog_1401853293","gog_2069637243","gog_1181223011","gog_1148469859","gog_2101535030","gog_2083450067","gog_1782940378","gog_1131896249","gog_1966636103","gog_1811802642","gog_1287772038","gog_1096594467","gog_1787145648","gog_1148628967","gog_1638974594","gog_1968762478","gog_1185548016","gog_1358910572","gog_1556475785","gog_1491593263","gog_1757241893","gog_1098095789","gog_1700881795","gog_2022730648","gog_2037300133","gog_1087469616","gog_1382196927","gog_1368540200","gog_1243164006","gog_1584411441","gog_1563371598","gog_1325854394","gog_1231341775","gog_1977280456","gog_1472911168","gog_1693738525","gog_2049238549","gog_1967929854","gog_1484878047","gog_1911573444","gog_1874062446","gog_1250236027","gog_1555686888","gog_1693399479","gog_1832279148","gog_1517244711","gog_1962625337","gog_1400510078","gog_2141696094","gog_1261760085","gog_2112984397","gog_2039122211","gog_1202579036","gog_1528793133","gog_1972529424","gog_1910291253","gog_2040576309","gog_1334672051","gog_1252116122","gog_1756871313","gog_1158117545","gog_1943690845","gog_1657733483","gog_1968139980","gog_1944497249","gog_1973532286","gog_1211172893","gog_1106494170","gog_1848509215","gog_1336312096","gog_1519319871","gog_1344048396","gog_1508978083","gog_1107374283","gog_1463541071","gog_1500636626","gog_1314201890","gog_1180864709","gog_1694304177","gog_1699285331","gog_1852100348","gog_1292330449","gog_1669798166","gog_1978808717","gog_1328256410","gog_1151620586","gog_1855332768","gog_1553651099","gog_1091471336","gog_1638629228","gog_1137397721","gog_1653861769","gog_1359653848","gog_1690569045","gog_1246925829","gog_1266297720","gog_1149072188","gog_1476939348","gog_2141074855","gog_1101087977","gog_1907950556","gog_2058163061","gog_1847037021","gog_1468179414","gog_1198606591","gog_1678796027","gog_1456958607","gog_1870479137","gog_2040489974","gog_1615731433","gog_2050304101","gog_1543692123","gog_1081512906","gog_1362149438","gog_1747942464","gog_1713818800","gog_1219860639","gog_1720505896","gog_1227949911","gog_1492080011","gog_1527747611","gog_1892417319","gog_1618509877","gog_1777375280","gog_1145557286","gog_1297567454","gog_1460624826","gog_1672944102","gog_2026547187","gog_1359755296","gog_1884844173","gog_1851697033","gog_1237801546","gog_1467822145","gog_1219733661","gog_1388105487","gog_1710802979","gog_1173648297","gog_2051366294","gog_1486505161","gog_1176670416","gog_1924408934","gog_1130984717","gog_1924593834","gog_1501787131","gog_1541627815","gog_1460940885","gog_1270720349","gog_1692190833","gog_1353775155","gog_1595080359","gog_1429088670","gog_1807139877","gog_1935316293","gog_1737719318","gog_2022798652","gog_1236852115","gog_1509990800","gog_1788776543","gog_1494449884","gog_1101966634","gog_1941207442","gog_1460498895","gog_1267516531","gog_2098373871","gog_2147435379","gog_2113673278","gog_1453542843","gog_1915782977","gog_1555346217","gog_1521207930","gog_1092791059","gog_1099434823","gog_1654929799","gog_2027965510","gog_1403031698","gog_1814774199","gog_1083509550","gog_1971885806","gog_2011710695","gog_2082796596","gog_1490910578","gog_1213387014","gog_2130482103","gog_1534687698","gog_2004013765","gog_1868113213","gog_1208328092","gog_1295858418","gog_1784222387","gog_1416951743","gog_1803751850","gog_1602153746","gog_1348134608","gog_1329332397","gog_1834193944","gog_1520199183","gog_1241087318","gog_2092457853","gog_2072442081","gog_1263987850","gog_2055573657","gog_1957993044","gog_1177428079","gog_1362009215","gog_1998991853","gog_1458564671","gog_2053414127","gog_1937635072","gog_1860895135","gog_1731355359","gog_1891422149","gog_1137907401","gog_1621125699","gog_1962732694","gog_1926058694","gog_1965604642","gog_2009769752","gog_1394062330","gog_1360756950","gog_1478256295","gog_1491864347","gog_1534298497","gog_1340635546","gog_1613674308","gog_1770509818","gog_1859471078","gog_2135171507","gog_1879811220","gog_1946714656","gog_2054035281","gog_1810787203","gog_1866211055","gog_1521201414","gog_2001530436","gog_1095749075","gog_1454257435","gog_1404572515","gog_1201640130","gog_1215205118","gog_2026659170","gog_2083035033","gog_1084720067","gog_1288989094","gog_1924037429","gog_1997205934","gog_2099798175","gog_2081194547","gog_1907927454","gog_1546007558","gog_1583790653","gog_1306058758","gog_1792834819","gog_1958733827","gog_1831295356","gog_1162224967","gog_1654294441","gog_1208378217","gog_1929045333","gog_1427526102","gog_1100467312","gog_1665394771","gog_1246953364","gog_2066013372","gog_2047535489","gog_2127416505","gog_1536350865","gog_2005931042","gog_1932188421","gog_1326039182","gog_1426352601","gog_1733361366","gog_2008763154","gog_1658226000","gog_1356902312","gog_2129979161","gog_1919788344","gog_1118354410","gog_1736830544","gog_2034417207","gog_1426171190","gog_1164547846","gog_1492176523","gog_1383352422","gog_1961754083","gog_1585979651","gog_1393390286","gog_1849441143","gog_1633308082","gog_1341832101","gog_2109116211","gog_1882468475","gog_1288901302","gog_1441797258","gog_2117186833","gog_1397801476","gog_1446213900","gog_1126467559"]}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1971477531',53,11111111111111111,'{"title":"GWENT: The Witcher Card Game"}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1971477531',54,NULL,'{"supported":[{"name":"Windows","slug":"windows","versions":null}]}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1971477531',91,11111111111111111,'{"criticsScore":85,"developers":["CD Projekt RED"],"genres":["Strategy"],"publishers":["CD Projekt RED"],"releaseDate":1540252800,"themes":["Fantasy"]}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1971477531',97,11111111111111111,'{"isModifiedByUser":false,"title":"GWENT The Witcher Card Game"}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_1971477531',99,11111111111111111,'{"title":"GWENT: The Witcher Card Game"}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_2074191081',47,NULL,'{"releases":["gog_1971477531","xboxone_606152324","psn_CUSA08234_00","psn_CUSA08213_00","xboxone_1671755991","psn_CUSA08159_00","test_CUSA08213_00","egg_gwent","steam_1284410","test_gfn_1284410","test_1284410","test_gog_1971477531","test_gfn_18541411","test_steam_1284410","test_18541411","gog_2074191081","generic_51152944180514264"]}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_2074191081',48,NULL,'{"dlcs":[]}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_2074191081',53,11111111111111111,'{"title":"GWENT: The Witcher Card Game (preview)"}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('gog_2074191081',54,NULL,'{"supported":[{"name":"Windows","slug":"windows","versions":null}]}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('steam_1091500',47,NULL,'{"releases":["gog_1423049311","steam_1091500","epic_Ginger","epic_GingerDummy","psn_CUSA16596_00","test_test_game_id","discord_364f18ab-253a-4d89-9f97-afb7ef302324","origin_2093619782","origin_1091500","steam_100004","origin_412023","xboxone_222473492","humble_cyberpunk2077_gog_keyless","stadia_stadia_cp77-stub","psn_CUSA16579_00","psn_CUSA16496_00","psn_CUSA16570_00","psn_CUSA18278_00","origin_1209025141","test_/cyberpunk-2077-free-latest-download/","test_1234","test_cyberpunk-2077","playfire_cyberpunk-2077","test_gog_cyberpunk2077","test_gfn_100838211","test_Cyberpunk 2077","psp_Cyberpunk 2077","psn_PPSA04029_00","psn_PPSA03974_00","psn_PPSA04027_00","psn_PPSA04028_00","test_gfn_101606111","psn_PPSA04026_00","test_PPSA04029_00","test_1091500","psn_CUSA20477_00","psn_CUSA25195_00","psn_CUSA24949_00","generic_51152725611062641"]}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('steam_1091500',48,NULL,'{"dlcs":[]}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('steam_1091500',53,11111111111111111,'{"title":"Cyberpunk 2077"}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('steam_1091500',54,NULL,'{"supported":[{"name":"Windows","slug":"windows","versions":null},{"name":"macOS","slug":"osx","versions":null}]}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('steam_1091500',91,11111111111111111,'{"criticsScore":75.2381,"developers":["CD Projekt RED"],"genres":["Role-playing (RPG)","Adventure","Shooter"],"publishers":["CD Projekt"],"releaseDate":1607558400,"themes":["Open world","Action","Science fiction","Sandbox"]}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('steam_1091500',97,11111111111111111,'{"isModifiedByUser":false,"title":"Cyberpunk 2077"}',NULL);
INSERT INTO "GamePieces" ("releaseKey","gamePieceTypeId","userId","value","languageId") VALUES ('steam_1091500',99,11111111111111111,'{"title":"Cyberpunk 2077"}',NULL);

CREATE TABLE 'GameTimes'(
	'userId' INT64 NOT NULL,
	'releaseKey' TEXT NOT NULL,
	'minutesInGame' INTEGER NOT NULL,
	CONSTRAINT 'FK_GameTimes_userId_Users_id'
		FOREIGN KEY ('userId')
		REFERENCES 'Users' ('id') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'FK_GameTimes_gameReleaseKey_ReleaseKeys_key'
		FOREIGN KEY ('releaseKey')
		REFERENCES 'ReleaseKeys' ('key') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'CK_GameTimes_minutesInGame'
		CHECK(minutesInGame >= 0),
	CONSTRAINT 'UK_GameTimes_userId_gameReleaseKey'
		UNIQUE ('userId', 'releaseKey')
);
INSERT INTO "GameTimes" ("userId","releaseKey","minutesInGame") VALUES (11111111111111111,'gog_1207664643',50);
INSERT INTO "GameTimes" ("userId","releaseKey","minutesInGame") VALUES (11111111111111111,'gog_1971477531',54);
INSERT INTO "GameTimes" ("userId","releaseKey","minutesInGame") VALUES (11111111111111111,'gog_1207658901',0);
INSERT INTO "GameTimes" ("userId","releaseKey","minutesInGame") VALUES (11111111111111111,'gog_1430742983',0);
INSERT INTO "GameTimes" ("userId","releaseKey","minutesInGame") VALUES (11111111111111111,'steam_1091500',0);

CREATE TABLE 'LastPlayedDates'(
	'userId' INT64 NOT NULL,
	'gameReleaseKey' TEXT NOT NULL,
	'lastPlayedDate' TEXT NOT NULL,
	CONSTRAINT 'FK_LastPlayedDates_userId_Users_id'
		FOREIGN KEY ('userId')
		REFERENCES 'Users' ('id') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'FK_LastPlayedDates_gameReleaseKey_ReleaseKeys_key'
		FOREIGN KEY ('gameReleaseKey')
		REFERENCES 'ReleaseKeys' ('key') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'UK_LastPlayedDates_userId_gameReleaseKey'
		UNIQUE ('userId', 'gameReleaseKey')
);
INSERT INTO "LastPlayedDates" ("userId","gameReleaseKey","lastPlayedDate") VALUES (11111111111111111,'gog_1207664643','2018-11-20 14:18:42');
INSERT INTO "LastPlayedDates" ("userId","gameReleaseKey","lastPlayedDate") VALUES (11111111111111111,'gog_1971477531','2017-07-01 03:32:16');

CREATE TABLE 'ProductPurchaseDates'(
	'gameReleaseKey' TEXT NOT NULL,
	'userId' INT64 NOT NULL,
	'purchaseDate' TEXT NULL,
	'addedDate' TEXT NULL,
	CONSTRAINT 'FK_ProductPurchaseDates_gameReleaseKey_ReleaseKeys_key'
		FOREIGN KEY ('gameReleaseKey')
		REFERENCES 'ReleaseKeys' ('key') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'FK_ProductPurchaseDates_userId_Users_id'
		FOREIGN KEY ('userId')
		REFERENCES 'Users' ('id') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'UK_ProductPurchaseDates_gameReleaseKey_userId'
		UNIQUE ('gameReleaseKey', 'userId')
);
INSERT INTO "ProductPurchaseDates" ("gameReleaseKey","userId","purchaseDate","addedDate") VALUES ('gog_1207658901',11111111111111111,'2013-12-13 17:43:49','2019-04-12 07:30:55');
INSERT INTO "ProductPurchaseDates" ("gameReleaseKey","userId","purchaseDate","addedDate") VALUES ('gog_1207664643',11111111111111111,'2015-07-23 22:30:48','2019-04-12 07:30:55');
INSERT INTO "ProductPurchaseDates" ("gameReleaseKey","userId","purchaseDate","addedDate") VALUES ('gog_1430742983',11111111111111111,'2016-06-19 04:06:07','2019-04-12 07:30:55');
INSERT INTO "ProductPurchaseDates" ("gameReleaseKey","userId","purchaseDate","addedDate") VALUES ('gog_1971477531',11111111111111111,'2016-11-28 17:40:14','2019-04-12 07:30:55');
INSERT INTO "ProductPurchaseDates" ("gameReleaseKey","userId","purchaseDate","addedDate") VALUES ('steam_1091500',11111111111111111,NULL,'2022-10-12 18:12:34');

CREATE TABLE 'Products'(
	'id' INTEGER NOT NULL PRIMARY KEY,
	'name' TEXT NULL,
	'parentId' INTEGER NULL,
	CONSTRAINT 'FK_Products_parentId_Products_id' FOREIGN KEY ('parentId')
		REFERENCES 'Products' ('id') ON DELETE CASCADE ON UPDATE CASCADE
	CONSTRAINT 'CK_Products_id_parentId' CHECK (id <> parentId)
);
INSERT INTO "Products" ("id","name","parentId") VALUES (1207658901,NULL,NULL);
INSERT INTO "Products" ("id","name","parentId") VALUES (1207664643,NULL,NULL);
INSERT INTO "Products" ("id","name","parentId") VALUES (1971477531,NULL,NULL);
INSERT INTO "Products" ("id","name","parentId") VALUES (2074191081,NULL,NULL);
INSERT INTO "Products" ("id","name","parentId") VALUES (1430742983,NULL,NULL);

CREATE TABLE 'ProductsToReleaseKeys'(
	'externalId' INTEGER NULL,
	'gogId' INTEGER NULL,
	'releaseKey' TEXT NOT NULL,
	CONSTRAINT 'UK_ProductsToReleaseKeys_externalId'
		UNIQUE ('externalId'),
	CONSTRAINT 'UK_ProductsToReleaseKeys_gogId'
		UNIQUE ('gogId'),
	CONSTRAINT 'FK_ProductsToReleaseKeys_externalId_InstalledExternalProducts_id'
		FOREIGN KEY ('externalId')
		REFERENCES 'InstalledExternalProducts' ('id') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'FK_ProductsToReleaseKeys_gogId_Products_id'
		FOREIGN KEY ('gogId')
		REFERENCES 'Products' ('id') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'FK_ProductsToReleaseKeys_releaseKey_ReleaseKeys_key'
		FOREIGN KEY ('releaseKey')
		REFERENCES 'ReleaseKeys' ('key') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'CK_ProductsToReleaseKeys_externalId_gogId'
		CHECK(
			externalId IS NOT NULL AND gogId IS NULL
			OR externalId IS NULL AND gogId IS NOT NULL
		),
	CONSTRAINT 'UK_ProductsToReleaseKeys_releaseKey'
		UNIQUE ('releaseKey'),
	CONSTRAINT 'CK_ProductsToReleaseKeys_releaseKey'
		CHECK(trim(releaseKey) <> '')
);
INSERT INTO "ProductsToReleaseKeys" ("externalId","gogId","releaseKey") VALUES (NULL,1207658901,'gog_1207658901');
INSERT INTO "ProductsToReleaseKeys" ("externalId","gogId","releaseKey") VALUES (NULL,1207664643,'gog_1207664643');
INSERT INTO "ProductsToReleaseKeys" ("externalId","gogId","releaseKey") VALUES (NULL,1971477531,'gog_1971477531');
INSERT INTO "ProductsToReleaseKeys" ("externalId","gogId","releaseKey") VALUES (NULL,2074191081,'gog_2074191081');

CREATE TABLE 'InstalledProducts'(
	'productId' INTEGER NOT NULL,
	CONSTRAINT 'FK_InstalledProducts_productId_Products_id' FOREIGN KEY ('productId')
		REFERENCES 'Products' ('id') ON DELETE RESTRICT ON UPDATE CASCADE,
	CONSTRAINT 'UQ_InstalledProducts_productId' UNIQUE ('productId')
);
INSERT INTO "InstalledProducts" ("productId") VALUES (1971477531);

CREATE TABLE 'InstalledBaseProducts'(
	'productId' INTEGER NOT NULL,
	'generation' INTEGER NOT NULL,
	'languageId' INTEGER NOT NULL,
	'installationPath' TEXT NOT NULL,
	'installationId' INT64 NOT NULL,
	'buildId' INT64 NULL,
	'branch' TEXT NULL,
	'installationDate' TEXT NULL,
	CONSTRAINT 'FK_InstalledBaseProducts_productId_buildId_languageId_AvailableLanguages_productId_buildId_languageId'
		FOREIGN KEY ('productId', 'buildId', 'languageId')
		REFERENCES 'AvailableLanguages' ('productId', 'buildId', 'languageId') ON DELETE RESTRICT ON UPDATE CASCADE,
	CONSTRAINT 'FK_InstalledBaseProducts_productId_InstalledProducts_productId'
		FOREIGN KEY ('productId')
		REFERENCES 'InstalledProducts' ('productId') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'UQ_InstalledBaseProducts_installationId' UNIQUE ('installationId'),
	CONSTRAINT 'UQ_InstalledBaseProducts_productId' UNIQUE ('productId')
);
INSERT INTO "InstalledBaseProducts" ("productId","generation","languageId","installationPath","installationId","buildId","branch","installationDate") VALUES (1971477531,2,16,'C:\Program Files\GOG Galaxy\Games\GWENT The Witcher Card Game',1234567890123456789,59534219748634025,NULL,'2026-08-26 06:17:36');

CREATE TABLE 'InstalledExternalProducts'(
	'id' INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
	'platformId' INTEGER NOT NULL,
	'productId' TEXT NOT NULL,
	CONSTRAINT 'FK_InstalledExternalProducts_platformId_Platforms_id'
		FOREIGN KEY ('platformId')
		REFERENCES 'Platforms' ('id') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'UK_InstalledExternalProducts_platformId_productId'
		UNIQUE ('platformId', 'productId')
);

CREATE TABLE 'ProductStates'(
	'productId' INTEGER NOT NULL,
	'installation' INTEGER NOT NULL,
	'operation' INTEGER NOT NULL,
	CONSTRAINT 'FK_ProductStates_productId_Products_id' FOREIGN KEY ('productId')
		REFERENCES 'Products' ('id') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'UQ_ProductStates_productId' UNIQUE ('productId')
);
INSERT INTO "ProductStates" ("productId","installation","operation") VALUES (1971477531,3,0);

CREATE TABLE 'PlayTaskTypes'(
	'id' INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
	'type' TEXT NOT NULL,
	CONSTRAINT 'UK_PlayTaskTypes_type'
		UNIQUE ('type'),
	CONSTRAINT 'CK_PlayTaskTypes_type'
		CHECK(trim(type) <> '')
);
INSERT INTO "PlayTaskTypes" ("id","type") VALUES (1,'BuiltInPrimary');

CREATE TABLE 'PlayTasks'(
	'id' INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
	'gameReleaseKey' TEXT NOT NULL,
	'userId' INT64 NULL,
	'order' INTEGER NOT NULL,
	'typeId' INTEGER NOT NULL,
	'isPrimary' BOOLEAN NOT NULL,
	CONSTRAINT 'FK_PlayTasks_gameReleaseKey_ReleaseKeys_key'
		FOREIGN KEY ('gameReleaseKey')
		REFERENCES 'ReleaseKeys' ('key') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'FK_PlayTasks_typeId_PlayTaskTypes_id'
		FOREIGN KEY ('typeId')
		REFERENCES 'PlayTaskTypes' ('id') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'FK_PlayTasks_userId_Users_id'
		FOREIGN KEY ('userId')
		REFERENCES 'Users' ('id') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'UK_PlayTasks_gameReleaseKey_userId_order'
		UNIQUE ('gameReleaseKey', 'userId', 'order')
);
INSERT INTO "PlayTasks" ("id","gameReleaseKey","userId","order","typeId","isPrimary") VALUES (1,'gog_1971477531',NULL,1,1,1);

CREATE TABLE 'PlayTaskLaunchParameters'(
	'playTaskId' INTEGER NOT NULL,
	'executablePath' TEXT NULL,
	'commandLineArgs' TEXT NULL,
	'label' TEXT NULL,
	CONSTRAINT 'FK_PlayTaskLaunchParameters_playTaskId_PlayTasks_id'
		FOREIGN KEY ('playTaskId')
		REFERENCES 'PlayTasks' ('id') ON DELETE CASCADE ON UPDATE CASCADE,
	CONSTRAINT 'UK_PlayTaskLaunchParameters_playTaskId'
		UNIQUE ('playTaskId')
);
INSERT INTO "PlayTaskLaunchParameters" ("playTaskId","executablePath","commandLineArgs","label") VALUES (1,'C:\Program Files\GOG Galaxy\Games\GWENT The Witcher Card Game\Gwent.exe','','GWINT: Wiedźmińska Gra Karciana');

COMMIT;
