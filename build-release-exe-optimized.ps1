# AnkiPlus MAUI EXE形式リリースビルドスクリプト（最適化版）

param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release"
)

Write-Host "AnkiPlus MAUI EXE形式リリースビルド（最適化版）を開始します..." -ForegroundColor Green
Write-Host "バージョン: $Version" -ForegroundColor Yellow
Write-Host "構成: $Configuration" -ForegroundColor Yellow

# プロジェクトをクリーンアップ
Write-Host "プロジェクトをクリーンアップしています..." -ForegroundColor Cyan
dotnet clean --configuration $Configuration --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "警告: クリーンアップでエラーが発生しましたが、続行します。" -ForegroundColor Yellow
}

# バージョン情報を更新
Write-Host "バージョン情報を更新しています..." -ForegroundColor Cyan
$versionParts = $Version.Split('.')
$buildNumber = [int]$versionParts[0] * 100 + [int]$versionParts[1] * 10 + [int]$versionParts[2]

# Windows用EXEをビルド（最適化版）
Write-Host "Windows用EXE実行ファイルをビルドしています（最適化版）..." -ForegroundColor Cyan
dotnet publish `
    --configuration $Configuration `
    --framework net9.0-windows10.0.19041.0 `
    --runtime win-x64 `
    --self-contained true `
    --output "bin\Release\exe-optimized" `
    -p:ApplicationDisplayVersion=$Version `
    -p:ApplicationVersion=$buildNumber `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -p:EnableCompressionInSingleFile=true `
    -p:WindowsPackageType=None

if ($LASTEXITCODE -ne 0) {
    Write-Host "エラー: ビルドが失敗しました。" -ForegroundColor Red
    exit 1
}

Write-Host "ビルドが正常に完了しました！" -ForegroundColor Green
Write-Host "出力フォルダ: bin\Release\exe-optimized\" -ForegroundColor Yellow

# 実行ファイルの確認
$exePath = "bin\Release\exe-optimized\AnkiPlus_MAUI.exe"
if (Test-Path $exePath) {
    $fileSize = [math]::Round((Get-Item $exePath).Length / 1MB, 2)
    Write-Host "最適化された実行ファイル: $exePath (${fileSize}MB)" -ForegroundColor Green
} else {
    Write-Host "警告: 実行ファイルが見つかりません。" -ForegroundColor Yellow
}

Write-Host "最適化版EXE形式リリースビルドが完了しました！" -ForegroundColor Green

Write-Host "配布方法:" -ForegroundColor Cyan
Write-Host "1. AnkiPlus_MAUI.exe を直接配布（コンパクト版）" -ForegroundColor White
Write-Host "2. GitHubリリースページでアップロード" -ForegroundColor White
Write-Host "3. ユーザーは警告を「詳細情報」→「実行」で回避可能" -ForegroundColor White

Write-Host "🎉 すべての処理が完了しました！" -ForegroundColor Green 