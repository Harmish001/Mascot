$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /optimize+ /target:winexe /platform:anycpu `
  /reference:System.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Web.Extensions.dll `
  /win32manifest:src\app.manifest `
  /out:mascot.exe src\Mascot.cs src\Avatar3D.cs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& .\mascot.exe --self-check
exit $LASTEXITCODE
