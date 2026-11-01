param([string]$Package = (Join-Path $PSScriptRoot '../dist/update-0.1.8.zip'))
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path $repo ('evidence/apex-map-import/native-support/legacy-update-'+(Get-Date -Format yyyyMMdd-HHmmss))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
foreach($file in @('AutoUpdater.cs','UpdateTrust.cs')) {
    $text=& git -C $repo show ('v0.1.7:src/RoweRC/'+$file)
    if($LASTEXITCODE){throw 'Cannot read the previous release updater'}
    [IO.File]::WriteAllLines((Join-Path $fixture $file),[string[]]$text,[Text.UTF8Encoding]::new($false))
}
@'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0-windows</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>
'@ | Set-Content (Join-Path $fixture 'LegacyUpdate.csproj')
@'
namespace RoweRC;
public static class MapCatalog {
 public const string SupportedBuild="1801ffbdfe";
 public const string SupportedExe="08da7cc9ca54d8f11dc05f70de7f33ed38014b33a82d2ccb4beb7f033592b4cf";
 public static string Hash(string path){using var f=File.OpenRead(path);return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(f)).ToLowerInvariant();}
}
public class SessionLog{public SessionLog(string path){}public void Write(string text){}}
'@ | Set-Content (Join-Path $fixture 'Stubs.cs')
@'
using RoweRC;
using System.Diagnostics;
using System.Text.Json;
var package=Path.GetFullPath(args[0]);var root=Path.GetFullPath(args[1]);
var release=AutoUpdater.Verify(File.ReadAllText(args[2]));
if(MapCatalog.Hash(package)!=release.Sha256)throw new Exception("Signed package checksum mismatch");
var changed=new Dictionary<string,string?>();
var files=AutoUpdater.InstallPayload(package,Path.Combine(root,"Game"),Path.Combine(root,"Backup"),changed);
var helper=Path.Combine(root,"Game/RCDriftSim/Binaries/Win64/RoweRC/RoweRC.exe");
var launch=new ProcessStartInfo(helper){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
launch.ArgumentList.Add("--prepare-terrain");launch.ArgumentList.Add(Path.Combine(root,"Game"));
using var p=Process.Start(launch)!;if(!p.WaitForExit(30000)||p.ExitCode!=0)throw new Exception("Updated helper could not extract terrain");
var data=Path.Combine(root,"Game/RCDriftSim/Binaries/Win64/RoweRC/Terrain/apex-wangan.rrcterrain");
if(MapCatalog.Hash(data)!="80e18b015d3fc1bd19808a1d565c085b09bcf2e83c154f644227891a83c85fe8")throw new Exception("Terrain extraction mismatch");
var report=JsonSerializer.Serialize(new{passed=true,previousUpdater="0.1.7",installed=release.Version,files=files.Count,terrainVerified=true});
File.WriteAllText(Path.Combine(root,"result.json"),report);Console.WriteLine(report);
'@ | Set-Content (Join-Path $fixture 'Program.cs')
& dotnet run --project (Join-Path $fixture 'LegacyUpdate.csproj') -c Release -- ([IO.Path]::GetFullPath($Package)) $fixture (Join-Path $repo 'dist/update.json')
if($LASTEXITCODE){throw 'Previous updater migration failed'}
Copy-Item -LiteralPath (Join-Path $fixture 'result.json') -Destination (Join-Path $repo 'evidence/apex-map-import/native-support/legacy-update-result.json') -Force
