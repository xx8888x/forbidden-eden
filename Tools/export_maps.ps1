# Exports the HS2 maps the game uses, one Unity launch each (scene bundles only open in play mode).
# Usage: powershell -File export_maps.ps1 [-Unity <path to Unity.exe>]
param([string]$Unity = "D:\Unity\Editor\6000.3.23f1\Editor\Unity.exe")
$game = Split-Path $PSScriptRoot -Parent
$project = Join-Path $game "ForbiddenEden"
$maps = @(
    "map/scene/34/hospitalroom.unity3d|Infirmary",
    "map/scene/gymnasiumwarehouse.unity3d|GymStorage",
    "map/scene/garden_suny.unity3d|Garden",
    "map/scene/myroom.unity3d|MyRoom",
    "map/scene/office.unity3d|Office",
    "map/scene/lobby.unity3d|Lobby",
    "map/scene/50/nightpark.unity3d|NightPark",
    "map/scene/50/church.unity3d|Church",
    "map/scene/50/train.unity3d|Train",
    "map/scene/34/chineserestaurant.unity3d|Restaurant",
    "map/scene/34/japanese.unity3d|Japanese"
)
foreach ($m in $maps) {
    $id = $m.Split("|")[1]
    $log = Join-Path $game "_map_$id.log"
    $p = Start-Process $Unity -ArgumentList "-batchmode", "-projectPath", "`"$project`"", "-executeMethod", "ForbiddenEden.EditorTools.Hs2SceneExporter.ExportFromArgs", "-map", "`"$m`"", "-logFile", "`"$log`"" -PassThru
    $p.WaitForExit()
    $ok = Test-Path (Join-Path $project "Assets\Resources\HS2\$id.prefab")
    "{0,-12} exit {1}  prefab {2}" -f $id, $p.ExitCode, $ok
}
