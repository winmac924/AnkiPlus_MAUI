using System.Text.Json;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace AnkiPlus_MAUI.Services
{
    public class CardSyncService
    {
        private readonly BlobStorageService _blobStorageService;
        private readonly string _localBasePath;
        private readonly string _tempBasePath;

        public CardSyncService(BlobStorageService blobStorageService)
        {
            _blobStorageService = blobStorageService;
            _localBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AnkiPlus");
            _tempBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnkiPlus");
        }

        private class CardInfo
        {
            public string Uuid { get; set; }
            public DateTime LastModified { get; set; }
            public string Content { get; set; }
        }

        private async Task<List<CardInfo>> ParseCardsFile(string content)
        {
            try
            {
                Debug.WriteLine($"ParseCardsFile開始 - コンテンツ長: {content.Length}");
                var cards = new List<CardInfo>();
                var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                Debug.WriteLine($"行数: {lines.Length}");

                // 1行目が数字のみの場合はカード数なのでスキップ
                int startIndex = 0;
                if (lines.Length > 0 && int.TryParse(lines[0], out _))
                {
                    startIndex = 1;
                }

                for (int i = startIndex; i < lines.Length; i++)
                {
                    try
                    {
                        // 行の末尾の改行文字を削除
                        var line = lines[i].TrimEnd('\r', '\n');
                        Debug.WriteLine($"行 {i} をパース: {line}");
                        var parts = line.Split(',');
                        if (parts.Length >= 2)
                        {
                            var card = new CardInfo
                            {
                                Uuid = parts[0],
                                LastModified = DateTime.ParseExact(parts[1].Trim(), "yyyy-MM-dd HH:mm:ss", null)
                            };
                            cards.Add(card);
                            Debug.WriteLine($"カード情報をパース: UUID={card.Uuid}, 最終更新={card.LastModified}");
                        }
                        else
                        {
                            Debug.WriteLine($"行 {i} のパースに失敗: カンマ区切りの値が不足しています");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"カードのパースに失敗: {lines[i]}, エラー: {ex.Message}");
                    }
                }

                Debug.WriteLine($"ParseCardsFile完了 - パースしたカード数: {cards.Count}");
                return cards;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"カードファイルのパース中にエラー: {ex.Message}");
                throw;
            }
        }

        private async Task<string> ReadLocalCardsFile(string notePath)
        {
            // 一時フォルダのパスを構築
            var tempDir = Path.Combine(_tempBasePath, Path.GetDirectoryName(notePath), Path.GetFileNameWithoutExtension(notePath) + "_temp");
            var cardsPath = Path.Combine(tempDir, "cards.txt");
            Debug.WriteLine($"読み込みファイルのパス: {cardsPath}");

            if (File.Exists(cardsPath))
            {
                return await File.ReadAllTextAsync(cardsPath);
            }
            return string.Empty;
        }

        public async Task SyncNoteAsync(string uid, string noteName, string subFolder = null)
        {
            try
            {
                // サーバーとローカルのパスを取得
                var userPath = subFolder != null ? $"{uid}/{subFolder}" : uid;
                var notePath = Path.Combine(userPath, noteName);

                Debug.WriteLine($"=== ノート同期開始 ===");
                Debug.WriteLine($"同期開始 - パス: {notePath}");

                // サーバーとローカルのcards.txtを取得
                var serverContent = await _blobStorageService.GetNoteContentAsync(uid, noteName, subFolder);
                Debug.WriteLine($"サーバーコンテンツ取得: {(serverContent != null ? "成功" : "失敗")}");
                
                var localContent = await ReadLocalCardsFile(subFolder != null ? Path.Combine(subFolder, noteName) : noteName);
                Debug.WriteLine($"ローカルコンテンツ取得: {(localContent != null ? "成功" : "失敗")}");
                Debug.WriteLine($"ローカルのcards.txtの内容:");
                Debug.WriteLine(localContent);

                // 一時ディレクトリの準備
                var tempDir = Path.Combine(_tempBasePath, subFolder ?? "", noteName + "_temp");
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                    Debug.WriteLine($"一時ディレクトリを作成: {tempDir}");
                }

                // ローカルにcards.txtがない場合、サーバーから全てダウンロード
                if (string.IsNullOrEmpty(localContent) && serverContent != null)
                {
                    Debug.WriteLine($"ローカルにcards.txtがないため、ノート全体をダウンロードします");
                    var serverCardsToDownload = await ParseCardsFile(serverContent);
                    
                    // サーバーのcards.txtをそのまま保存
                    var tempCardsPath = Path.Combine(tempDir, "cards.txt");
                    await File.WriteAllTextAsync(tempCardsPath, serverContent);
                    Debug.WriteLine($"一時フォルダにcards.txtを保存: {tempCardsPath}");

                    // カードファイルをダウンロード
                    foreach (var card in serverCardsToDownload)
                    {
                        var cardContent = await _blobStorageService.GetNoteContentAsync(uid, $"{card.Uuid}.json", $"{subFolder}/{noteName}/cards");
                        if (cardContent != null)
                        {
                            var tempCardPath = Path.Combine(tempDir, "cards", $"{card.Uuid}.json");
                            var tempCardDir = Path.GetDirectoryName(tempCardPath);
                            if (!Directory.Exists(tempCardDir))
                            {
                                Directory.CreateDirectory(tempCardDir);
                                Debug.WriteLine($"一時カードディレクトリを作成: {tempCardDir}");
                            }
                            await File.WriteAllTextAsync(tempCardPath, cardContent);
                            Debug.WriteLine($"カードファイルを一時フォルダにダウンロード: {tempCardPath}");
                        }
                    }

                    // imgフォルダの同期（カードの同期とは別に実行）
                    var tempImgDir = Path.Combine(tempDir, "img");
                    Debug.WriteLine($"=== 画像ダウンロード処理開始 ===");
                    Debug.WriteLine($"一時画像フォルダのパス: {tempImgDir}");
                    Debug.WriteLine($"一時画像フォルダの存在確認: {Directory.Exists(tempImgDir)}");

                    if (!Directory.Exists(tempImgDir))
                    {
                        Directory.CreateDirectory(tempImgDir);
                        Debug.WriteLine($"一時imgディレクトリを作成: {tempImgDir}");
                    }

                    // imgフォルダ内のファイル一覧を取得
                    var imgFiles = await _blobStorageService.GetNoteListAsync(uid, $"{subFolder}/{noteName}/img");
                    Debug.WriteLine($"サーバーの画像ファイル数: {imgFiles.Count}");
                    Debug.WriteLine($"サーバーの画像ファイル一覧:");
                    foreach (var imgFile in imgFiles)
                    {
                        Debug.WriteLine($"- {imgFile}");
                    }

                    foreach (var imgFile in imgFiles)
                    {
                        // iOS版の形式（img_########_######.jpg）をチェック
                        if (Regex.IsMatch(imgFile, @"^img_\d{8}_\d{6}\.jpg$"))
                    {
                        Debug.WriteLine($"画像ファイルの処理開始: {imgFile}");
                        var imgFileContent = await _blobStorageService.GetNoteContentAsync(uid, imgFile, $"{subFolder}/{noteName}/img");
                        if (imgFileContent != null)
                            {
                                try
                        {
                            var tempImgPath = Path.Combine(tempImgDir, imgFile);
                                    Debug.WriteLine($"画像ファイルの保存先: {tempImgPath}");
                                    var imgBytes = Convert.FromBase64String(imgFileContent);
                                    Debug.WriteLine($"画像ファイルのサイズ: {imgBytes.Length} バイト");
                                    await File.WriteAllBytesAsync(tempImgPath, imgBytes);
                            Debug.WriteLine($"画像ファイルを一時フォルダにダウンロード: {tempImgPath}");
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"画像ファイルのダウンロード中にエラー: {imgFile}, エラー: {ex.Message}");
                                    Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                                }
                        }
                        else
                        {
                            Debug.WriteLine($"画像ファイルのコンテンツが取得できません: {imgFile}");
                            }
                        }
                    }

                    // .ankplsファイルを作成
                    var localNotePath = Path.Combine(_localBasePath, subFolder ?? "", $"{noteName}.ankpls");
                    var localNoteDir = Path.GetDirectoryName(localNotePath);
                    if (!Directory.Exists(localNoteDir))
                    {
                        Directory.CreateDirectory(localNoteDir);
                        Debug.WriteLine($"ローカルのノートディレクトリを作成: {localNoteDir}");
                    }

                    if (File.Exists(localNotePath))
                    {
                        File.Delete(localNotePath);
                    }
                    ZipFile.CreateFromDirectory(tempDir, localNotePath);
                    Debug.WriteLine($".ankplsファイルを作成: {localNotePath}");

                    Debug.WriteLine($"ノート '{noteName}' の全体ダウンロードが完了しました。");
                    return;
                }

                // ローカルにcards.txtがある場合
                if (!string.IsNullOrEmpty(localContent))
                {
                    Debug.WriteLine($"=== ローカルにcards.txtが存在する場合の処理開始 ===");
                    var localCards = await ParseCardsFile(localContent);
                    var serverCards = serverContent != null ? await ParseCardsFile(serverContent) : new List<CardInfo>();

                    // サーバーのcards.txtを一時保存
                    if (serverContent != null)
                    {
                        var serverCardsPath = Path.Combine(tempDir, "server_cards.txt");
                        await File.WriteAllTextAsync(serverCardsPath, serverContent);
                        Debug.WriteLine($"サーバーのcards.txtを一時保存: {serverCardsPath}");
                        Debug.WriteLine($"サーバーのcards.txtの内容:");
                        Debug.WriteLine(serverContent);
                    }

                    var cardsToDownload = new List<CardInfo>();
                    var cardsToUpload = new List<CardInfo>();
                    var updatedLocalCards = localCards.ToList();

                    Debug.WriteLine($"ローカルのカード情報:");
                    foreach (var card in localCards)
                    {
                        Debug.WriteLine($"UUID={card.Uuid}, 最終更新={card.LastModified}");
                    }

                    Debug.WriteLine($"サーバーのカード情報:");
                    foreach (var card in serverCards)
                    {
                        Debug.WriteLine($"UUID={card.Uuid}, 最終更新={card.LastModified}");
                    }

                    // サーバーにあるがローカルにない、または更新が必要なカードを特定
                    foreach (var serverCard in serverCards)
                    {
                        var localCard = localCards.FirstOrDefault(c => c.Uuid == serverCard.Uuid);
                        if (localCard == null || localCard.LastModified < serverCard.LastModified)
                        {
                            cardsToDownload.Add(serverCard);
                            // ローカルのリストを更新
                            if (localCard != null)
                            {
                                updatedLocalCards.Remove(localCard);
                            }
                            updatedLocalCards.Add(serverCard);
                        }
                    }

                    // ローカルにあるがサーバーにない、または更新が必要なカードを特定
                    foreach (var localCard in localCards)
                    {
                        var serverCard = serverCards.FirstOrDefault(c => c.Uuid == localCard.Uuid);
                        if (serverCard == null || serverCard.LastModified < localCard.LastModified)
                        {
                            cardsToUpload.Add(localCard);
                        }
                    }

                    // カードのダウンロード
                    if (cardsToDownload.Any())
                    {
                        Debug.WriteLine($"ダウンロードするカード数: {cardsToDownload.Count}");
                        foreach (var card in cardsToDownload)
                        {
                            var cardContent = await _blobStorageService.GetNoteContentAsync(uid, $"{card.Uuid}.json", $"{subFolder}/{noteName}/cards");
                            if (cardContent != null)
                            {
                                var tempCardPath = Path.Combine(tempDir, "cards", $"{card.Uuid}.json");
                                var tempCardDir = Path.GetDirectoryName(tempCardPath);
                                if (!Directory.Exists(tempCardDir))
                                {
                                    Directory.CreateDirectory(tempCardDir);
                                    Debug.WriteLine($"一時カードディレクトリを作成: {tempCardDir}");
                                }
                                await File.WriteAllTextAsync(tempCardPath, cardContent);
                                Debug.WriteLine($"カードファイルを一時フォルダにダウンロード: {tempCardPath}");
                            }
                            }
                        }

                    // imgフォルダの同期（カードの同期とは独立して実行）
                        var tempImgDir = Path.Combine(tempDir, "img");
                    Debug.WriteLine($"=== 画像同期処理開始（ローカルにcards.txt存在時） ===");
                    Debug.WriteLine($"一時画像フォルダのパス: {tempImgDir}");

                        if (!Directory.Exists(tempImgDir))
                        {
                            Directory.CreateDirectory(tempImgDir);
                            Debug.WriteLine($"一時imgディレクトリを作成: {tempImgDir}");
                        }

                    // ローカルのimgフォルダのパスを取得
                    var localCardsPath = Path.Combine(_tempBasePath, subFolder ?? "", noteName + "_temp", "cards.txt");
                    var localImgDir = Path.Combine(Path.GetDirectoryName(localCardsPath), "img");
                    Debug.WriteLine($"ローカルのimgフォルダのパス: {localImgDir}");
                    Debug.WriteLine($"ローカルのimgフォルダの存在確認: {Directory.Exists(localImgDir)}");

                    if (Directory.Exists(localImgDir))
                    {
                        var localImgFiles = Directory.GetFiles(localImgDir, "img_*.jpg");
                        Debug.WriteLine($"ローカルの画像ファイル数: {localImgFiles.Length}");
                        Debug.WriteLine($"ローカルの画像ファイル一覧:");
                        foreach (var imgFile in localImgFiles)
                        {
                            Debug.WriteLine($"- {Path.GetFileName(imgFile)}");
                        }

                        foreach (var imgFile in localImgFiles)
                        {
                            try
                            {
                                var fileName = Path.GetFileName(imgFile);
                                // iOS版の形式（img_########_######.jpg）をチェック
                                if (Regex.IsMatch(fileName, @"^img_\d{8}_\d{6}\.jpg$"))
                                {
                                    Debug.WriteLine($"画像ファイルの処理開始: {fileName}");
                                    var imgBytes = await File.ReadAllBytesAsync(imgFile);
                                    Debug.WriteLine($"画像ファイルのサイズ: {imgBytes.Length} バイト");
                                    await _blobStorageService.UploadImageBinaryAsync(uid, fileName, imgBytes, $"{subFolder}/{noteName}/img");
                                    Debug.WriteLine($"画像ファイルをアップロード: {fileName}");

                                    // 一時フォルダにもコピー
                                    var tempImgPath = Path.Combine(tempImgDir, fileName);
                                    await File.WriteAllBytesAsync(tempImgPath, imgBytes);
                                    Debug.WriteLine($"画像ファイルを一時フォルダにコピー: {tempImgPath}");
                                }
                                else
                                {
                                    Debug.WriteLine($"画像ファイル名の形式が正しくありません: {fileName}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"画像ファイルの処理中にエラー: {imgFile}, エラー: {ex.Message}");
                                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                            }
                        }
                    }

                    // サーバーの画像ファイルを取得
                    var serverImgFiles = await _blobStorageService.GetNoteListAsync(uid, $"{subFolder}/{noteName}/img");
                    Debug.WriteLine($"サーバーの画像ファイル数: {serverImgFiles.Count}");
                    Debug.WriteLine($"サーバーの画像ファイル一覧:");
                    foreach (var imgFile in serverImgFiles)
                    {
                        Debug.WriteLine($"- {imgFile}");
                    }

                    foreach (var imgFile in serverImgFiles)
                    {
                        // iOS版の形式（img_########_######.jpg）をチェック
                        if (Regex.IsMatch(imgFile, @"^img_\d{8}_\d{6}\.jpg$"))
                        {
                            Debug.WriteLine($"画像ファイルの処理開始: {imgFile}");
                            var imgFileContent = await _blobStorageService.GetNoteContentAsync(uid, imgFile, $"{subFolder}/{noteName}/img");
                            if (imgFileContent != null)
                            {
                                try
                            {
                                var tempImgPath = Path.Combine(tempImgDir, imgFile);
                                    Debug.WriteLine($"画像ファイルの保存先: {tempImgPath}");
                                    var imgBytes = Convert.FromBase64String(imgFileContent);
                                    Debug.WriteLine($"画像ファイルのサイズ: {imgBytes.Length} バイト");
                                    await File.WriteAllBytesAsync(tempImgPath, imgBytes);
                                Debug.WriteLine($"画像ファイルを一時フォルダにダウンロード: {tempImgPath}");
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"画像ファイルのダウンロード中にエラー: {imgFile}, エラー: {ex.Message}");
                                    Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                                }
                            }
                            else
                            {
                                Debug.WriteLine($"画像ファイルのコンテンツが取得できません: {imgFile}");
                            }
                        }
                    }
                    Debug.WriteLine($"=== 画像同期処理完了（ローカルにcards.txt存在時） ===");

                    // カードのアップロード
                    if (cardsToUpload.Any())
                    {
                        Debug.WriteLine($"アップロードするカード数: {cardsToUpload.Count}");
                        foreach (var card in cardsToUpload)
                        {
                            // ローカルのcardsフォルダからJSONファイルを読み込む
                            var localCardPath = Path.Combine(_tempBasePath, subFolder ?? "", noteName + "_temp", "cards", $"{card.Uuid}.json");
                            Debug.WriteLine($"カードファイルのパス: {localCardPath}");
                            
                            if (File.Exists(localCardPath))
                            {
                                var cardContent = await File.ReadAllTextAsync(localCardPath);
                                // カードのJSONファイルを直接アップロード
                                await _blobStorageService.SaveNoteAsync(uid, $"{card.Uuid}.json", cardContent, $"{subFolder}/{noteName}/cards");
                                Debug.WriteLine($"カードファイルをアップロード: {localCardPath}");
                            }
                            else
                            {
                                Debug.WriteLine($"カードファイルが見つかりません: {localCardPath}");
                            }
                        }

                        // ローカルのcards.txtをサーバーにアップロード
                        var newContent = string.Join("\n", localCards.Select(c => $"{c.Uuid},{c.LastModified:yyyy-MM-dd HH:mm:ss}"));
                        var contentWithCount = $"{localCards.Count}\n{newContent}";
                        await _blobStorageService.SaveNoteAsync(uid, noteName, contentWithCount, subFolder);
                        Debug.WriteLine($"サーバーにcards.txtをアップロード（カードのアップロード時）");
                    }

                    // ローカルのcards.txtを更新（ダウンロードしたカードがある場合）
                    if (cardsToDownload.Any())
                    {
                        var newContent = string.Join("\n", updatedLocalCards.Select(c => $"{c.Uuid},{c.LastModified:yyyy-MM-dd HH:mm:ss}"));
                        var contentWithCount = $"{updatedLocalCards.Count}\n{newContent}";
                        var tempCardsPath = Path.Combine(tempDir, "cards.txt");
                        await File.WriteAllTextAsync(tempCardsPath, contentWithCount);
                        Debug.WriteLine($"一時フォルダにcards.txtを保存: {tempCardsPath}");

                        // サーバーにアップロード
                        await _blobStorageService.SaveNoteAsync(uid, noteName, contentWithCount, subFolder);
                        Debug.WriteLine($"サーバーにcards.txtをアップロード（カードのダウンロード時）");
                    }
                    else if (!cardsToUpload.Any())
                    {
                        Debug.WriteLine($"ローカルとサーバーの内容が同じため、cards.txtのアップロードは不要です");
                    }
                    Debug.WriteLine($"=== ローカルにcards.txtが存在する場合の処理完了 ===");
                }

                Debug.WriteLine($"ノート '{noteName}' の同期が完了しました。");
                Debug.WriteLine($"=== ノート同期完了 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ノートの同期中にエラー: {ex.Message}");
                throw;
            }
        }

        public async Task SyncAllNotesAsync(string uid)
        {
            try
            {
                Debug.WriteLine($"=== 全ノート同期開始 ===");
                // サブフォルダ内のノートを同期
                var subFolders = await _blobStorageService.GetSubFoldersAsync(uid);
                Debug.WriteLine($"取得したサブフォルダ数: {subFolders.Count}");

                foreach (var subFolder in subFolders)
                {
                    Debug.WriteLine($"サブフォルダの同期開始: {subFolder}");
                    var subFolderNotes = await _blobStorageService.GetNoteListAsync(uid, subFolder);
                    Debug.WriteLine($"サブフォルダ内のノート数: {subFolderNotes.Count}");

                    foreach (var note in subFolderNotes)
                    {
                        Debug.WriteLine($"ノートの同期開始: {note}");
                        // ノート名は親フォルダ名を使用
                        var noteName = Path.GetFileName(Path.GetDirectoryName($"{subFolder}/{note}/cards.txt"));
                        await SyncNoteAsync(uid, noteName, subFolder);
                    }
                }
                Debug.WriteLine($"=== 全ノート同期完了 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"全ノートの同期中にエラー: {ex.Message}");
                throw;
            }
        }

        public async Task SyncLocalNotesAsync(string uid)
        {
            try
            {
                Debug.WriteLine($"=== ローカルノートの同期開始 ===");

                // 1. サーバーとローカル両方にあるノートの同期
                Debug.WriteLine("1. 両方にあるノートの同期開始");
                var serverNotes = await _blobStorageService.GetNoteListAsync(uid);
                var localNotes = new List<(string subFolder, string noteName, string cardsPath)>();
                var tempNotes = new List<(string subFolder, string noteName, string cardsPath)>();

                // ローカルノートの収集
                var localBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AnkiPlus");
                var tempBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnkiPlus");

                Debug.WriteLine($"ローカルベースパス: {localBasePath}");
                Debug.WriteLine($"tempベースパス: {tempBasePath}");

                // ローカルのノートを収集
                if (Directory.Exists(localBasePath))
                {
                    Debug.WriteLine($"ローカルフォルダが存在します: {localBasePath}");
                    foreach (var subFolder in Directory.GetDirectories(localBasePath))
                    {
                        var subFolderName = Path.GetFileName(subFolder);
                        Debug.WriteLine($"ローカルサブフォルダを検索中: {subFolderName}");
                        foreach (var noteFolder in Directory.GetDirectories(subFolder))
                        {
                            var noteName = Path.GetFileName(noteFolder);
                            var cardsPath = Path.Combine(noteFolder, "cards.txt");
                            if (File.Exists(cardsPath))
                            {
                                localNotes.Add((subFolderName, noteName, cardsPath));
                                Debug.WriteLine($"ローカルノートを追加: {subFolderName}/{noteName}");
                            }
                        }
                    }
                }
                else
                {
                    Debug.WriteLine($"ローカルフォルダが存在しません: {localBasePath}");
                }

                // tempフォルダのノートを収集（直接_tempで終わるフォルダとサブフォルダ内の_tempフォルダの両方を検索）
                if (Directory.Exists(tempBasePath))
                {
                    Debug.WriteLine($"tempフォルダが存在します: {tempBasePath}");
                    
                    // tempBasePath直下の_tempで終わるフォルダを検索
                    foreach (var tempFolder in Directory.GetDirectories(tempBasePath, "*_temp"))
                    {
                        var noteName = Path.GetFileName(tempFolder);
                        if (noteName.EndsWith("_temp"))
                        {
                            noteName = noteName.Substring(0, noteName.Length - 5);
                            var cardsPath = Path.Combine(tempFolder, "cards.txt");
                            if (File.Exists(cardsPath))
                            {
                                tempNotes.Add((null, noteName, cardsPath));
                                Debug.WriteLine($"tempノートを追加（ルート）: {noteName}");
                            }
                        }
                    }
                    
                    // サブフォルダ内の_tempで終わるフォルダを検索
                    foreach (var subFolder in Directory.GetDirectories(tempBasePath))
                    {
                        var subFolderName = Path.GetFileName(subFolder);
                        // _tempで終わるフォルダはスキップ（すでに上で処理済み）
                        if (subFolderName.EndsWith("_temp"))
                            continue;
                            
                        Debug.WriteLine($"tempサブフォルダを検索中: {subFolderName}");
                        foreach (var noteFolder in Directory.GetDirectories(subFolder))
                        {
                            var noteName = Path.GetFileName(noteFolder);
                            if (noteName.EndsWith("_temp"))
                            {
                                noteName = noteName.Substring(0, noteName.Length - 5);
                                var cardsPath = Path.Combine(noteFolder, "cards.txt");
                                if (File.Exists(cardsPath))
                                {
                                    tempNotes.Add((subFolderName, noteName, cardsPath));
                                    Debug.WriteLine($"tempノートを追加: {subFolderName}/{noteName}");
                                }
                            }
                        }
                    }
                }
                else
                {
                    Debug.WriteLine($"tempフォルダが存在しません: {tempBasePath}");
                }

                Debug.WriteLine($"収集したローカルノート数: {localNotes.Count}");
                Debug.WriteLine($"収集したtempノート数: {tempNotes.Count}");
                Debug.WriteLine($"サーバーノート数: {serverNotes.Count}");

                // 両方にあるノートの同期
                foreach (var serverNote in serverNotes)
                {
                    var localNote = localNotes.FirstOrDefault(n => n.noteName == serverNote);
                    var tempNote = tempNotes.FirstOrDefault(n => n.noteName == serverNote);

                    if (localNote.noteName != null || tempNote.noteName != null)
                    {
                        Debug.WriteLine($"両方にあるノートを同期: {serverNote}");
                        await SyncNoteAsync(uid, serverNote, localNote.subFolder ?? tempNote.subFolder);
                    }
                }

                // 2. サーバーにのみあるノートのダウンロード
                Debug.WriteLine("2. サーバーにのみあるノートのダウンロード開始");
                foreach (var serverNote in serverNotes)
                {
                    if (!localNotes.Any(n => n.noteName == serverNote) && !tempNotes.Any(n => n.noteName == serverNote))
                    {
                        Debug.WriteLine($"サーバーにのみあるノートをダウンロード: {serverNote}");
                        await SyncNoteAsync(uid, serverNote, null);
                    }
                }

                // 3. ローカルにのみあるノートのアップロード
                Debug.WriteLine("3. ローカルにのみあるノートのアップロード開始");
                var allLocalNotes = localNotes.Concat(tempNotes).DistinctBy(n => n.noteName);
                foreach (var localNote in allLocalNotes)
                {
                    if (!serverNotes.Contains(localNote.noteName))
                    {
                        Debug.WriteLine($"ローカルにのみあるノートをアップロード開始: {localNote.noteName}");
                        Debug.WriteLine($"ノートのパス: {localNote.cardsPath}");
                        Debug.WriteLine($"サブフォルダ: {localNote.subFolder ?? "なし"}");
                        
                        var localContent = await File.ReadAllTextAsync(localNote.cardsPath);
                        Debug.WriteLine($"cards.txtの内容: {localContent}");
                        
                        // cards.txtのパスからcardsディレクトリのパスを取得
                        var cardsDir = Path.Combine(Path.GetDirectoryName(localNote.cardsPath), "cards");
                        Debug.WriteLine($"カードディレクトリのパス: {cardsDir}");

                        if (Directory.Exists(cardsDir))
                        {
                            var jsonFiles = Directory.GetFiles(cardsDir, "*.json");
                            Debug.WriteLine($"見つかったJSONファイル数: {jsonFiles.Length}");
                            var cardCount = jsonFiles.Length;
                            var cardLines = new List<string>();

                            foreach (var jsonFile in jsonFiles)
                            {
                                try
                                {
                                    var jsonContent = await File.ReadAllTextAsync(jsonFile);
                                    var jsonFileName = Path.GetFileName(jsonFile);
                                    var uuid = Path.GetFileNameWithoutExtension(jsonFileName);
                                    var lastModified = File.GetLastWriteTime(jsonFile);
                                    cardLines.Add($"{uuid},{lastModified:yyyy-MM-dd HH:mm:ss}");
                                    Debug.WriteLine($"カード情報を準備: {uuid}, {lastModified:yyyy-MM-dd HH:mm:ss}");

                                    // サブフォルダのパスを正しく構築
                                    var uploadPath = localNote.subFolder != null 
                                        ? $"{localNote.subFolder}/{localNote.noteName}/cards"
                                        : $"{localNote.noteName}/cards";
                                    
                                    await _blobStorageService.SaveNoteAsync(uid, jsonFileName, jsonContent, uploadPath);
                                    Debug.WriteLine($"カードファイルをアップロード: {jsonFileName} -> {uploadPath}");
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"カードファイルのアップロード中にエラー: {jsonFile}, エラー: {ex.Message}");
                                    Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                                }
                            }

                            // 画像ファイルもアップロード
                            var imgDir = Path.Combine(Path.GetDirectoryName(localNote.cardsPath), "img");
                            Debug.WriteLine($"画像ディレクトリのパス: {imgDir}");
                            if (Directory.Exists(imgDir))
                            {
                                var imgFiles = Directory.GetFiles(imgDir, "img_*.jpg");
                                Debug.WriteLine($"見つかった画像ファイル数: {imgFiles.Length}");
                                
                                foreach (var imgFile in imgFiles)
                                {
                                    try
                                    {
                                        var imgFileName = Path.GetFileName(imgFile);
                                        var imgBytes = await File.ReadAllBytesAsync(imgFile);
                                        var uploadImgPath = localNote.subFolder != null 
                                            ? $"{localNote.subFolder}/{localNote.noteName}/img"
                                            : $"{localNote.noteName}/img";
                                        
                                        await _blobStorageService.UploadImageBinaryAsync(uid, imgFileName, imgBytes, uploadImgPath);
                                        Debug.WriteLine($"画像ファイルをアップロード: {imgFileName} -> {uploadImgPath}");
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"画像ファイルのアップロード中にエラー: {imgFile}, エラー: {ex.Message}");
                                    }
                                }
                            }
                            else
                            {
                                Debug.WriteLine($"画像ディレクトリが見つかりません: {imgDir}");
                            }

                            // cards.txtの内容を正しい形式に変換
                            var formattedContent = $"{cardCount}\n{string.Join("\n", cardLines)}";
                            Debug.WriteLine($"アップロード用のcards.txt内容:");
                            Debug.WriteLine(formattedContent);
                            
                            await _blobStorageService.SaveNoteAsync(uid, localNote.noteName, formattedContent, localNote.subFolder);
                            Debug.WriteLine($"cards.txtをアップロード: {localNote.noteName} -> サブフォルダ: {localNote.subFolder ?? "ルート"}");
                        }
                        else
                        {
                            Debug.WriteLine($"カードディレクトリが見つかりません: {cardsDir}");
                        }
                        
                        Debug.WriteLine($"ローカルにのみあるノートのアップロード完了: {localNote.noteName}");
                    }
                    else
                    {
                        Debug.WriteLine($"ノート '{localNote.noteName}' はサーバーにも存在するため、アップロードをスキップ");
                    }
                }

                Debug.WriteLine($"=== ローカルノートの同期完了 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ローカルノートの同期中にエラー: {ex.Message}");
                throw;
            }
        }
    }
} 