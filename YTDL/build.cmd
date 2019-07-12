dotnet tool install --global dotnet-warp
md .\bin\Release\sfe\
dotnet warp -o .\bin\Release\sfe\YTDL.exe
xcopy /y .\appsettings.json .\bin\Release\sfe\
