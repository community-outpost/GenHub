# Authentication and Authorization
For authenticating and authorizing accounts

## ConnectionString
1. Provide your ConnectionString in line 08 program.cs file by adding appsettings.json file and a your database connection string.
2. Migrations are already added run "dotnet ef database update" command and the DB will be created.
3. Add your own Issuer, Audience and a at least 32 characters Security key in appsettings.json "Jwt" Json object 