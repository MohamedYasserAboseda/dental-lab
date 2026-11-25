@echo off
echo === Dental Lab - setup ===
dotnet --version || (echo .NET SDK not found. Install .NET 10 SDK first. & pause & exit /b 1)
dotnet tool install --global dotnet-ef 2>nul || dotnet tool update --global dotnet-ef
dotnet restore DentalLab.slnx || (pause & exit /b 1)

set MIG=src\DentalLab.Infrastructure\Persistence\Migrations
if not exist %MIG% (
  echo Creating InitialCreate migration...
  dotnet ef migrations add InitialCreate --project src/DentalLab.Infrastructure --startup-project src/DentalLab.Desktop --output-dir Persistence/Migrations || (pause & exit /b 1)
  goto :build
)

call :ensure AddDoctorsAndLedger || (pause & exit /b 1)
call :ensure AddPatientsCatalogCases || (pause & exit /b 1)
call :ensure AddBilling || (pause & exit /b 1)
call :ensure AddAttachments || (pause & exit /b 1)

:build
dotnet build DentalLab.slnx -c Debug || (pause & exit /b 1)
echo === Build OK. Run run.bat ===
pause
exit /b 0

:ensure
dir /b %MIG%\*_%1.cs >nul 2>nul && exit /b 0
echo Creating migration %1 ...
dotnet ef migrations add %1 --project src/DentalLab.Infrastructure --startup-project src/DentalLab.Desktop --output-dir Persistence/Migrations
exit /b %errorlevel%
