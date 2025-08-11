# Authentication and Authorization
For authenticating and authorizing accounts

## ConnectionString
I hardcoded connection strings in line 21 AuthModule.cs since no appsettings.json is available.
Put your strings in Key vaults or anywhere you feel safe and access them there.  

## Migration
Migrations are already added run "dotnet ef database update" command and the DB will be created.

## Issuer, Audience, Secret Key
Add your own Issuer, Audience and a Security key containing at least 32 characters in AuthModule line 96.
Store your security key in key vaults.
In line 80 AuthModule and in line 26 TokenRepository.cs I hardcoded an example key.
In line 31,32 hardcoded Issuer and Audience. Put your own. Isseuer should be the one who creates the token and Audience is where the token can be used.

## Json Web Token Expire time
To change the expire date of Json web token, change time in line 34 in TokenRepository.

## User given Role verification
I verified provided roles for "Reader and Writer" in line 41 AuthController. You should add your own safe Roles present in the IdentityRole table.

## Rate Limiting
If a user fails to give correct password 5 times in a row then he wwill be locked out for one hour. If you want to change the time, It in line 66 program.cs.