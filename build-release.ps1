# AnkiPlus MAUI - Windows リリースビルドスクリプト (GitHub対応)

param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release",
    [switch]$CreateTag = $false,
    [switch]$PushToGitHub = $false
)

Write-Host "AnkiPlus MAUI リリースビルドを開始します..." -ForegroundColor Green
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

# Windows用ビルド（MSIX）
Write-Host "Windows用MSIXパッケージをビルドしています..." -ForegroundColor Blue
dotnet publish -f net9.0-windows10.0.19041.0 -c $Configuration -p:RuntimeIdentifierOverride=win10-x64 -p:WindowsPackageType=MSIX

# ビルド結果の確認
$outputPath = "bin\$Configuration\net9.0-windows10.0.19041.0\win10-x64\publish\"
if (Test-Path $outputPath) {
    Write-Host "ビルドが正常に完了しました！" -ForegroundColor Green
    Write-Host "出力フォルダ: $outputPath" -ForegroundColor Yellow
    
    # MSIXファイルの検索
    $msixFiles = Get-ChildItem -Path $outputPath -Filter "*.msix" -Recurse
    if ($msixFiles.Count -gt 0) {
        Write-Host "生成されたMSIXファイル:" -ForegroundColor Green
        foreach ($file in $msixFiles) {
            Write-Host "  $($file.FullName)" -ForegroundColor Yellow
        }
    }
} else {
    Write-Host "エラー: ビルドが失敗しました。" -ForegroundColor Red
    exit 1
}

Write-Host "リリースビルドが完了しました！" -ForegroundColor Green

# オプション: 署名の確認
Write-Host "`n署名の確認:" -ForegroundColor Blue
Write-Host "MSIXファイルには有効な証明書で署名することを忘れずに！" -ForegroundColor Yellow
Write-Host "開発証明書の場合、配布前に信頼済み証明書での再署名が必要です。" -ForegroundColor Yellow

# GitHub関連の処理
if ($CreateTag -or $PushToGitHub) {
    Write-Host "`nGitHub処理:" -ForegroundColor Blue
    
    # Gitの状態確認
    $gitStatus = git status --porcelain
    if ($gitStatus) {
        Write-Host "⚠️ コミットされていない変更があります:" -ForegroundColor Yellow
        Write-Host $gitStatus -ForegroundColor Yellow
        
        $continue = Read-Host "続行しますか？ (y/N)"
        if ($continue -ne "y" -and $continue -ne "Y") {
            Write-Host "処理を中断しました。" -ForegroundColor Red
            exit 1
        }
    }
    
    if ($CreateTag) {
        Write-Host "Gitタグを作成しています..." -ForegroundColor Blue
        $tagName = "v$Version"
        
        # タグの存在確認
        $existingTag = git tag -l $tagName
        if ($existingTag) {
            Write-Host "⚠️ タグ '$tagName' は既に存在します。" -ForegroundColor Yellow
            $overwrite = Read-Host "タグを上書きしますか？ (y/N)"
            if ($overwrite -eq "y" -or $overwrite -eq "Y") {
                git tag -d $tagName
                Write-Host "既存のタグを削除しました。" -ForegroundColor Yellow
            } else {
                Write-Host "タグ作成をスキップしました。" -ForegroundColor Yellow
                $CreateTag = $false
            }
        }
        
        if ($CreateTag) {
            git tag -a $tagName -m "Release version $Version"
            Write-Host "✅ タグ '$tagName' を作成しました。" -ForegroundColor Green
        }
    }
    
    if ($PushToGitHub) {
        Write-Host "GitHubにプッシュしています..." -ForegroundColor Blue
        
        # 変更をコミット（もしあれば）
        if ($gitStatus) {
            Write-Host "変更をコミットしています..." -ForegroundColor Blue
            git add .
            git commit -m "Release version $Version"
        }
        
        # masterブランチにプッシュ
        git push origin main
        Write-Host "✅ コードをGitHubにプッシュしました。" -ForegroundColor Green
        
        # タグもプッシュ
        if ($CreateTag) {
            git push origin $tagName
            Write-Host "✅ タグ '$tagName' をGitHubにプッシュしました。" -ForegroundColor Green
            Write-Host "🚀 GitHub Actionsが自動的にリリースを作成します！" -ForegroundColor Green
        }
    }
}

Write-Host "`n🎉 すべての処理が完了しました！" -ForegroundColor Green

if ($CreateTag -and $PushToGitHub) {
    Write-Host "📖 GitHubリリースページを確認してください:" -ForegroundColor Yellow
    Write-Host "   https://github.com/winmac924/AnkiPlus_MAUI/releases" -ForegroundColor Cyan
} 