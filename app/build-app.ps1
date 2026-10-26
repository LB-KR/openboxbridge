<#
  Builds OpenBoxBridge.exe (WinForms, .NET Framework 4.8) with the C# compiler that ships
  inside PowerShell 7. No Visual Studio needed.

    pwsh ./app/build-app.ps1                      # on Windows: uses the installed .NET Framework
    pwsh ./app/build-app.ps1 -RefDir <folder>     # or a folder of .NET 4.8 reference assemblies
                                                  # (e.g. github.com/mono/reference-assemblies v4.8)
  Output: out/OpenBoxBridge.exe
#>
param([string]$RefDir = "", [string]$Out = "")
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Out) { $Out = Join-Path (Split-Path -Parent $here) "out/OpenBoxBridge.exe" }
if (-not $RefDir) {
  foreach ($d in @("${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8",
                   "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319")) { if ($d -and (Test-Path "$d\mscorlib.dll")) { $RefDir = $d; break } }
}
if (-not $RefDir -or -not (Test-Path (Join-Path $RefDir "mscorlib.dll"))) { throw "No .NET Framework 4.8 assemblies found - pass -RefDir" }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Out) | Out-Null
$refs = [System.Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
'mscorlib.dll','System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll' | ForEach-Object {
  $refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path $RefDir $_))) }
$trees = [System.Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
Get-ChildItem (Join-Path $here "src/*.cs") | ForEach-Object { $trees.Add(
  [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($_.FullName),
    [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::new([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::CSharp7_3), $_.FullName)) }
$opts = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::WindowsApplication).WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release)
$comp = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create("OpenBoxBridge", $trees, $refs, $opts)
$ico = [IO.File]::OpenRead((Join-Path $here "app.ico")); $man = [IO.File]::OpenRead((Join-Path $here "app.manifest"))
$win32 = $comp.CreateDefaultWin32Resources($true, $false, $man, $ico)
$fs = [IO.File]::Create($Out); $res = $comp.Emit($fs, $null, $null, $win32); $fs.Close(); $ico.Close(); $man.Close()
$res.Diagnostics | Where-Object { $_.Severity -eq 'Error' } | ForEach-Object { $_.ToString() }
if ($res.Success) { "Built $Out" } else { Remove-Item $Out; throw "Build failed" }
