@echo off
REM Usage: migrate.bat MigrationName
if "%~1"=="" (echo Usage: migrate.bat MigrationName & exit /b 1)
dotnet ef migrations add %1 --project src/DentalLab.Infrastructure --startup-project src/DentalLab.Desktop --output-dir Persistence/Migrations
