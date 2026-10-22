param([string]$Destination = '', [switch]$SkipModels)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$Destination) { $Destination = Join-Path $projectRoot 'LocalAssistant' }
$Destination = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$downloadRoot = Join-Path $Destination 'downloads'
New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null
function Get-AssistantFile([string]$Url, [string]$Path, [string]$Sha256 = '') {
    if ((Test-Path -LiteralPath $Path) -and (!$Sha256 -or (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Sha256)) { return }
    Write-Output ('Downloading ' + [IO.Path]::GetFileName($Path))
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile ($Path + '.partial') -TimeoutSec 1800
    if ($Sha256 -and (Get-FileHash -LiteralPath ($Path + '.partial') -Algorithm SHA256).Hash -ne $Sha256) { throw "Checksum mismatch: $Path" }
    Move-Item -LiteralPath ($Path + '.partial') -Destination $Path -Force
}
if (!$SkipModels) {
    $llamaZip = Join-Path $downloadRoot 'llama-b11811-bin-win-vulkan-x64.zip'
    Get-AssistantFile 'https://github.com/ggml-org/llama.cpp/releases/download/b11811/llama-b11811-bin-win-vulkan-x64.zip' $llamaZip '92fd55324c0e9f3660a5b0a7e9a98941e749b7172d50520b9b190c8e297c5c3a'
    Expand-Archive -LiteralPath $llamaZip -DestinationPath (Join-Path $Destination 'llama') -Force
    $asrName = 'sherpa-onnx-streaming-zipformer-small-ctc-zh-int8-2025-04-01'
    $asrArchive = Join-Path $downloadRoot ($asrName + '.tar.bz2')
    Get-AssistantFile ('https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/' + $asrName + '.tar.bz2') $asrArchive 'b3b309f7ce4a737195fcc6963ea19b0653a7d3401580af5ae0d3e284cbb71f0b'
    & tar.exe -xf $asrArchive -C $Destination
    if ($LASTEXITCODE -ne 0) { throw 'Speech model extraction failed.' }
    Get-AssistantFile 'https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf' (Join-Path $Destination 'dialogue.gguf') '6a1a2eb6d15622bf3c96857206351ba97e1af16c30d7a74ee38970e434e9407e'
}
& dotnet publish (Join-Path $PSScriptRoot 'Blacktide.LocalAssistant.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $Destination 'host')
if ($LASTEXITCODE -ne 0) { throw 'Local assistant build failed.' }
Write-Output "LOCAL_ASSISTANT_SETUP_PASS: $Destination"
