# AnkiPlus MAUI EXE形式リリースビルドスクリプト

param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release"
)

Write-Host "AnkiPlus MAUI EXE形式リリースビルドを開始します..." -ForegroundColor Green
Write-Host "バージョン: $Version" -ForegroundColor Yellow
Write-Host "構成: $Configuration" -ForegroundColor Yellow

# プロジェクトをクリーンアップ
Write-Host "プロジェクトをクリーンアップしています..." -ForegroundColor Cyan
dotnet clean --configuration $Configuration --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "エラー: クリーンアップが失敗しました。" -ForegroundColor Red
    exit 1
}

# バージョン情報を更新
Write-Host "バージョン情報を更新しています..." -ForegroundColor Cyan
$versionParts = $Version.Split('.')
$buildNumber = [int]$versionParts[0] * 100 + [int]$versionParts[1] * 10 + [int]$versionParts[2]

# Windows用EXEをビルド
Write-Host "Windows用EXE実行ファイルをビルドしています..." -ForegroundColor Cyan
dotnet publish `
    --configuration $Configuration `
    --framework net9.0-windows10.0.19041.0 `
    --runtime win10-x64 `
    --self-contained true `
    --output "bin\Release\exe-publish" `
    -p:ApplicationDisplayVersion=$Version `
    -p:ApplicationVersion=$buildNumber `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -p:WindowsPackageType=None

if ($LASTEXITCODE -ne 0) {
    Write-Host "エラー: ビルドが失敗しました。" -ForegroundColor Red
    exit 1
}

Write-Host "ビルドが正常に完了しました！" -ForegroundColor Green
Write-Host "出力フォルダ: bin\Release\exe-publish\" -ForegroundColor Yellow

# 実行ファイルの確認
$exePath = "bin\Release\exe-publish\AnkiPlus_MAUI.exe"
if (Test-Path $exePath) {
    $fileSize = [math]::Round((Get-Item $exePath).Length / 1MB, 2)
    Write-Host "実行ファイル: $exePath (${fileSize}MB)" -ForegroundColor Green
} else {
    Write-Host "警告: 実行ファイルが見つかりません。" -ForegroundColor Yellow
}

Write-Host "EXE形式リリースビルドが完了しました！" -ForegroundColor Green

Write-Host "配布方法:" -ForegroundColor Cyan
Write-Host "1. AnkiPlus_MAUI.exe を直接配布" -ForegroundColor White
Write-Host "2. 必要に応じてインストーラーを作成" -ForegroundColor White
Write-Host "3. GitHubリリースページでアップロード" -ForegroundColor White

Write-Host "🎉 すべての処理が完了しました！" -ForegroundColor Green 