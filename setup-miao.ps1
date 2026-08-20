# setup-miao.ps1
# Tao solution Miao tren o E voi cau truc: Core / UI / Desktop / Android
# Chay: powershell -ExecutionPolicy Bypass -File setup-miao.ps1

$ErrorActionPreference = "Stop"

$root = "E:\Miao"

Write-Host "== Tao thu muc goc: $root ==" -ForegroundColor Cyan
New-Item -ItemType Directory -Path $root -Force | Out-Null
Set-Location $root

Write-Host "== Cai template Avalonia (neu chua co) ==" -ForegroundColor Cyan
dotnet new install Avalonia.Templates

Write-Host "== Tao solution ==" -ForegroundColor Cyan
dotnet new sln -n Miao

Write-Host "== Tao Miao.Core (class library, logic tai truyen) ==" -ForegroundColor Cyan
dotnet new classlib -n Miao.Core -o Miao.Core -f net10.0
dotnet sln add Miao.Core

Write-Host "== Tao Miao.UI + Miao.Desktop + Miao.Android tu template Avalonia mvvm ==" -ForegroundColor Cyan
# Template nay se tu sinh project UI dung chung + Desktop + Android trong thu muc hien tai
dotnet new avalonia.mvvm -o . --Desktop true --Android true --iOS false --Browser false

Write-Host ""
Write-Host "== LUU Y ==" -ForegroundColor Yellow
Write-Host "Template avalonia.mvvm dat ten project theo output folder / prompt cua no."
Write-Host "Neu ten project sinh ra KHONG phai Miao.UI / Miao.Desktop / Miao.Android,"
Write-Host "hay doi ten thu muc + .csproj + namespace ben trong cho khop, roi chay tiep doan duoi."
Write-Host ""

Write-Host "== Them cac project con lai vao solution ==" -ForegroundColor Cyan
Get-ChildItem -Path $root -Recurse -Filter "*.csproj" | ForEach-Object {
    dotnet sln add $_.FullName
}

Write-Host "== Thiet lap reference giua cac project ==" -ForegroundColor Cyan
$core    = Get-ChildItem -Recurse -Filter "Miao.Core.csproj"    | Select-Object -First 1
$ui      = Get-ChildItem -Recurse -Filter "Miao.UI.csproj"      | Select-Object -First 1
$desktop = Get-ChildItem -Recurse -Filter "Miao.Desktop.csproj" | Select-Object -First 1
$android = Get-ChildItem -Recurse -Filter "Miao.Android.csproj" | Select-Object -First 1

if ($ui -and $core) {
    dotnet add $ui.FullName reference $core.FullName
    Write-Host "  Miao.UI -> Miao.Core  OK" -ForegroundColor Green
} else {
    Write-Host "  [!] Khong tim thay Miao.UI hoac Miao.Core, ban tu them reference sau." -ForegroundColor Red
}

if ($desktop -and $ui) {
    dotnet add $desktop.FullName reference $ui.FullName
    Write-Host "  Miao.Desktop -> Miao.UI  OK" -ForegroundColor Green
} else {
    Write-Host "  [!] Khong tim thay Miao.Desktop hoac Miao.UI, ban tu them reference sau." -ForegroundColor Red
}

if ($android -and $ui) {
    dotnet add $android.FullName reference $ui.FullName
    Write-Host "  Miao.Android -> Miao.UI  OK" -ForegroundColor Green
} else {
    Write-Host "  [!] Khong tim thay Miao.Android hoac Miao.UI, ban tu them reference sau." -ForegroundColor Red
}

Write-Host ""
Write-Host "== Build thu toan bo solution ==" -ForegroundColor Cyan
dotnet build

Write-Host ""
Write-Host "== XONG. Mo project bang: code $root ==" -ForegroundColor Green
Write-Host "Chay thu Desktop: dotnet run --project Miao.Desktop"
