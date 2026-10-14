$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'CommandAtlas/CommandAtlas.csproj'
dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
$build = Join-Path $PSScriptRoot 'CommandAtlas/bin/Release'
$artifacts = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$target = Join-Path $artifacts 'CommandAtlas-1.0.0.0.zip'
Add-Type -AssemblyName System.IO.Compression
$stream = [System.IO.File]::Open($target, [System.IO.FileMode]::Create)
$zip = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in @('CommandAtlas.dll','CommandAtlas.deps.json','CommandAtlas.json','LICENSE','NOTICE','images/icon.png')) {
        $source = Join-Path $build $name
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $source, $name, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $zip.Dispose(); $stream.Dispose() }
Get-FileHash -LiteralPath $target -Algorithm SHA256
