# AnkiPlus MAUI - Windows EXE 最適化リリースビルドスクリプト

param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release"
)

Write-Host "AnkiPlus MAUI 最適化EXE リリースビルドを開始します..." -ForegroundColor Green
Write-Host "バージョン: $Version" -ForegroundColor Yellow
Write-Host "構成: $Configuration" -ForegroundColor Yellow

# ビルド前のクリーンアップ
Write-Host "プロジェクトをクリーンアップしています..." -ForegroundColor Blue
dotnet clean -c $Configuration

# バージョン情報の更新
Write-Host "バージョン情報を更新しています..." -ForegroundColor Blue
$csprojPath = "AnkiPlus_MAUI.csproj"
$csprojContent = Get-Content $csprojPath -Raw
$csprojContent = $csprojContent -replace '<ApplicationDisplayVersion>.*?</ApplicationDisplayVersion>', "<ApplicationDisplayVersion>$Version</ApplicationDisplayVersion>"
$versionNumber = [int]($Version.Replace('.', ''))
$csprojContent = $csprojContent -replace '<ApplicationVersion>.*?</ApplicationVersion>', "<ApplicationVersion>$versionNumber</ApplicationVersion>"
Set-Content $csprojPath $csprojContent

# Windows用最適化ビルド（サイズ最適化）
Write-Host "Windows用最適化実行ファイルをビルドしています..." -ForegroundColor Blue
Write-Host "⚠️ 最適化ビルドは時間がかかる場合があります..." -ForegroundColor Yellow

dotnet publish -f net9.0-windows10.0.19041.0 -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:ApplicationDisplayVersion=$Version `
    -p:PublishTrimmed=true `
    -p:TrimMode=partial `
    -p:EnableCompressionInSingleFile=true `
    -p:DebuggerSupport=false `
    -p:EnableUnsafeUTF7Encoding=false `
    -p:InvariantGlobalization=false `
    -p:OptimizationPreference=Size

# ビルド結果の確認
$outputPath = "bin\$Configuration\net9.0-windows10.0.19041.0\win-x64\publish\"
if (Test-Path $outputPath) {
    Write-Host "ビルドが正常に完了しました！" -ForegroundColor Green
    Write-Host "出力フォルダ: $outputPath" -ForegroundColor Yellow
    
    # EXEファイルの検索
    $exeFiles = Get-ChildItem -Path $outputPath -Filter "*.exe" -Recurse
    if ($exeFiles.Count -gt 0) {
        Write-Host "生成された最適化実行ファイル:" -ForegroundColor Green
        foreach ($file in $exeFiles) {
            $fileSize = [math]::Round($file.Length / 1MB, 2)
            Write-Host "  📁 $($file.FullName)" -ForegroundColor Yellow
            Write-Host "  📊 ファイルサイズ: $fileSize MB (最適化済み)" -ForegroundColor Cyan
            Write-Host "  📅 作成日時: $($file.CreationTime)" -ForegroundColor Gray
        }
        
        # リリースフォルダの作成
        $releaseFolder = "Release_Optimized_v$Version"
        if (Test-Path $releaseFolder) {
            Remove-Item $releaseFolder -Recurse -Force
        }
        New-Item -ItemType Directory -Path $releaseFolder -Force | Out-Null
        
        # EXEファイルをリリースフォルダにコピー
        foreach ($file in $exeFiles) {
            $newFileName = "AnkiPlus_MAUI_Optimized_v$Version.exe"
            Copy-Item $file.FullName -Destination "$releaseFolder\$newFileName"
            Write-Host "  ✅ コピー完了: $releaseFolder\$newFileName" -ForegroundColor Green
        }
        
        # README.txtの作成
        $readmeContent = @"
AnkiPlus MAUI v$Version (最適化版)
==================================

📥 インストール方法:
1. AnkiPlus_MAUI_Optimized_v$Version.exe をダブルクリック
2. 必要に応じて「不明な発行元」の警告を許可
3. インストール不要で直接実行可能

🚀 最適化について:
- ファイルサイズを最小化
- 起動時間を短縮
- 未使用コードを除去（Trimming）
- 圧縮による最適化

🔧 システム要件:
- Windows 10 (バージョン 1809 以降) または Windows 11
- .NET Runtime不要（自己完結型実行ファイル）

⚠️ セキュリティについて:
- 初回実行時にWindows Defenderの警告が表示される場合があります
- 「詳細情報」→「実行」をクリックして続行してください

⚠️ 最適化版の注意事項:
- 一部の機能が制限される場合があります
- 問題が発生した場合は通常版をご利用ください

🔄 アップデート:
- アプリ内で自動的に新しいバージョンを検出します
- 新しいバージョンが利用可能な場合、自動ダウンロードされます

📞 サポート:
- GitHub: https://github.com/winmac924/AnkiPlus_MAUI
- Issues: https://github.com/winmac924/AnkiPlus_MAUI/issues

作成日時: $(Get-Date -Format "yyyy年MM月dd日 HH:mm:ss")
"@
        
        Set-Content -Path "$releaseFolder\README.txt" -Value $readmeContent -Encoding UTF8
        Write-Host "  📝 README.txt を作成しました" -ForegroundColor Green
        
        Write-Host "`n🎉 最適化リリースパッケージが完成しました！" -ForegroundColor Green
        Write-Host "📂 リリースフォルダ: $releaseFolder" -ForegroundColor Yellow
        
    } else {
        Write-Host "⚠️ 実行ファイルが見つかりませんでした。" -ForegroundColor Yellow
    }
} else {
    Write-Host "❌ エラー: ビルドが失敗しました。" -ForegroundColor Red
    exit 1
}

Write-Host "`n✨ 最適化EXEリリースビルドが完了しました！" -ForegroundColor Green

# 署名に関する情報
Write-Host "`n🔐 署名について:" -ForegroundColor Blue
Write-Host "実行ファイルには有効な証明書で署名することを推奨します。" -ForegroundColor Yellow
Write-Host "署名されていない場合、初回実行時にWindows Defenderの警告が表示される可能性があります。" -ForegroundColor Yellow
Write-Host "詳細: https://docs.microsoft.com/ja-jp/windows/win32/appxpkg/how-to-sign-a-package-using-signtool" -ForegroundColor Gray

Write-Host "`n📊 最適化の効果:" -ForegroundColor Blue
Write-Host "通常版と最適化版のファイルサイズを比較してください。" -ForegroundColor Yellow
Write-Host "最適化により、30-50%のサイズ削減が期待できます。" -ForegroundColor Yellow 