# Authentication and Authorization
For authenticating and authorizing accounts

## ConnectionString
Provide your ConnectionString in line 08 program.cs file by adding appsettings.json file and a your database connection string.

## Migration
Migrations are already added run "dotnet ef database update" command and the DB will be created.

## Issuer, Audience, Secret Key
Add your own Issuer, Audience and a Security key containing at least 32 characters in appsettings.json "Jwt" Json object.

## Json Web Token Expire time
To change the expire date of Json web token, change time in line 39 in TokenRepository.

## User given Role verification
I verified provided roles for "Reader and Writer" in line 42 AuthController. You should add your own safe Roles present in the IdentityRole table.

## Rate Limiting
If a user fails to give correct password 5times in a row then he wwill be locked out for one hour. If you want to change the time in line 64 program.cs.