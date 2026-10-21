@echo off
rem Builds ready-to-share ShyFlow.exe files into the "out" folder.
rem 1) light: small file, needs .NET 8 Desktop Runtime installed on the user's PC
dotnet publish Island\Island.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o out\light
rem 2) full: works everywhere without installing anything, but the file is large
dotnet publish Island\Island.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out\full
echo.
echo Done. Look in the out folder.
pause
