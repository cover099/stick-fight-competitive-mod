# Installs BepInEx 5 (if needed) and CompetitiveMod into Stick Fight: The Game.
# Run Install.bat (double-click) rather than this file directly.

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$AppId       = '674940'
$GameFolder  = 'StickFightTheGame'
$BepInExUrl  = 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip'
$ModUrl      = 'https://github.com/cover099/stick-fight-competitive-mod/releases/latest/download/CompetitiveMod.dll'
$ModFile     = 'CompetitiveMod.dll'
$OldModFiles = @('FpsFix.dll')

function Write-Step($text) { Write-Host "`n> $text" -ForegroundColor Cyan }
function Fail($text) {
    Write-Host "`nERROR: $text" -ForegroundColor Red
    exit 1
}

# --- Find the game ----------------------------------------------------------------------------
function Get-SteamLibraries {
    $steam = $null
    foreach ($key in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
        $props = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
        if ($props.SteamPath) { $steam = $props.SteamPath; break }
        if ($props.InstallPath) { $steam = $props.InstallPath; break }
    }
    if (-not $steam) { return @() }
    $libraries = @($steam -replace '/', '\')
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        foreach ($match in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
            $libraries += ($match.Groups[1].Value -replace '\\\\', '\')
        }
    }
    return $libraries | Select-Object -Unique
}

function Find-Game {
    foreach ($library in Get-SteamLibraries) {
        $path = Join-Path $library "steamapps\common\$GameFolder"
        if (Test-Path (Join-Path $path 'StickFight.exe')) { return $path }
    }
    return $null
}

Write-Host 'Stick Fight CompetitiveMod installer' -ForegroundColor Green

Write-Step 'Looking for Stick Fight: The Game...'
$game = Find-Game
if (-not $game) {
    Write-Host 'Could not find the game automatically.'
    Write-Host 'In Steam: right-click Stick Fight > Manage > Browse local files, then copy that folder''s path.'
    $game = (Read-Host 'Paste the game folder path here').Trim('" ')
}
if (-not (Test-Path (Join-Path $game 'StickFight.exe'))) {
    Fail "StickFight.exe was not found in '$game'."
}
Write-Host "Found: $game"

if (Get-Process -Name 'StickFight' -ErrorAction SilentlyContinue) {
    Fail 'Stick Fight is running. Close the game and run the installer again.'
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ('sf-compmod-' + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $temp | Out-Null

try {
    # --- BepInEx --------------------------------------------------------------------------------
    if (Test-Path (Join-Path $game 'BepInEx\core\BepInEx.dll')) {
        Write-Step 'BepInEx is already installed, skipping.'
    } else {
        Write-Step 'Downloading BepInEx 5.4.23.5...'
        $zip = Join-Path $temp 'BepInEx.zip'
        Invoke-WebRequest -Uri $BepInExUrl -OutFile $zip -UseBasicParsing
        Write-Step 'Installing BepInEx...'
        Expand-Archive -Path $zip -DestinationPath $game -Force
    }

    # --- The mod --------------------------------------------------------------------------------
    $plugins = Join-Path $game 'BepInEx\plugins'
    New-Item -ItemType Directory -Path $plugins -Force | Out-Null
    foreach ($old in $OldModFiles) {
        $oldPath = Join-Path $plugins $old
        if (Test-Path $oldPath) {
            Remove-Item $oldPath -Force
            Write-Host "Removed old $old"
        }
    }

    # Use the DLL next to this script if there is one (release zip), otherwise download the latest.
    $local = Join-Path $PSScriptRoot $ModFile
    if (Test-Path $local) {
        Write-Step "Installing $ModFile..."
        Copy-Item $local (Join-Path $plugins $ModFile) -Force
    } else {
        Write-Step "Downloading the latest $ModFile..."
        Invoke-WebRequest -Uri $ModUrl -OutFile (Join-Path $plugins $ModFile) -UseBasicParsing
    }
} catch {
    Fail $_.Exception.Message
} finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "`nDone! Start Stick Fight from Steam as usual." -ForegroundColor Green
Write-Host 'The first start with BepInEx takes a little longer while it sets itself up.'
Write-Host 'The mod starts switched OFF: flip the COMP MOD lever on the first screen, or type /comp in chat.'
