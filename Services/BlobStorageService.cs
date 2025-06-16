using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.Diagnostics;
using System.IO;

namespace AnkiPlus_MAUI.Services
{
    public class BlobStorageService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private const string CONTAINER_NAME = "flashnote";
        private bool _isInitialized = false;
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AnkiPlus");

        public BlobStorageService()
        {
            _blobServiceClient = App.BlobServiceClient;
        }

        private async Task EnsureInitializedAsync()
        {
            if (!_isInitialized)
            {
                await InitializeContainerAsync();
                _isInitialized = true;
            }
        }

        private async Task InitializeContainerAsync()
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                
                if (!await containerClient.ExistsAsync())
                {
                    Debug.WriteLine($"コンテナ '{CONTAINER_NAME}' が存在しないため、作成します。");
                    await containerClient.CreateAsync();
                    Debug.WriteLine($"コンテナ '{CONTAINER_NAME}' を作成しました。");
                }
                else
                {
                    Debug.WriteLine($"コンテナ '{CONTAINER_NAME}' は既に存在します。");
                }

                Debug.WriteLine("利用可能なコンテナ一覧:");
                await foreach (var container in _blobServiceClient.GetBlobContainersAsync())
                {
                    Debug.WriteLine($"- {container.Name}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"コンテナの初期化中にエラー: {ex.Message}");
                throw;
            }
        }

        private string GetUserPath(string uid, string subFolder = null)
        {
            return subFolder != null ? $"{uid}/{subFolder}" : uid;
        }

        private (string subFolder, string noteName, bool isCard) ParseBlobPath(string blobPath)
        {
            var parts = blobPath.Split('/');
            if (parts.Length < 3) return (null, null, false);

            // UIDを除外し、残りのパスを解析
            var remainingParts = parts.Skip(1).ToArray();
            var noteName = remainingParts[remainingParts.Length - 1];
            var subFolder = string.Join("/", remainingParts.Take(remainingParts.Length - 1));

            // cards.txtの場合、親フォルダをノート名として扱う
            if (noteName == "cards.txt")
            {
                var parentFolder = remainingParts[remainingParts.Length - 2];
                return (subFolder, parentFolder, true);
            }

            // cards/ディレクトリ内のJSONファイルの場合
            if (remainingParts.Length > 2 && remainingParts[remainingParts.Length - 2] == "cards")
            {
                return (subFolder, noteName, true);
            }

            return (subFolder, noteName, false);
        }

        public async Task<List<string>> GetNoteListAsync(string uid, string subFolder = null)
        {
            await EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"ノート一覧の取得開始 - UID: {uid}, サブフォルダ: {subFolder ?? "なし"}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                var userPath = GetUserPath(uid, subFolder);
                var notes = new List<string>();
                var processedNames = new HashSet<string>();

                await foreach (var blob in containerClient.GetBlobsAsync(prefix: userPath))
                {
                    Debug.WriteLine($"見つかったBlob: {blob.Name}");
                    
                    // cards.txtのみを処理
                    if (blob.Name.EndsWith("/cards.txt"))
                    {
                        var (parsedSubFolder, noteName, _) = ParseBlobPath(blob.Name);
                        if (noteName != null && !processedNames.Contains(noteName))
                        {
                            notes.Add(noteName);
                            processedNames.Add(noteName);
                            Debug.WriteLine($"追加されたノート: {noteName} (パス: {blob.Name})");
                        }
                    }
                }

                Debug.WriteLine($"取得したノート数: {notes.Count}");
                return notes;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ノート一覧の取得中にエラー: {ex.Message}");
                throw;
            }
        }

        public async Task<string> GetNoteContentAsync(string uid, string noteName, string subFolder = null)
        {
            await EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"ノートの取得開始 - UID: {uid}, ノート名: {noteName}, サブフォルダ: {subFolder ?? "なし"}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                var userPath = GetUserPath(uid, subFolder);
                string fullPath;

                // cards.txtの場合
                if (noteName.EndsWith(".json"))
                {
                    fullPath = $"{userPath}/{noteName}";
                }
                else
                {
                    fullPath = $"{userPath}/{noteName}/cards.txt";
                }

                var blobClient = containerClient.GetBlobClient(fullPath);

                if (await blobClient.ExistsAsync())
                {
                    var response = await blobClient.DownloadAsync();
                    using var streamReader = new StreamReader(response.Value.Content);
                    var content = await streamReader.ReadToEndAsync();
                    Debug.WriteLine($"ノートの取得完了 - サイズ: {content.Length} バイト, パス: {fullPath}");
                    return content;
                }

                Debug.WriteLine($"ノートが見つかりません: {fullPath}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ノートの取得中にエラー: {ex.Message}");
                throw;
            }
        }

        public async Task SaveNoteAsync(string uid, string noteName, string content, string subFolder = null)
        {
            await EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"ノートの保存開始 - UID: {uid}, ノート名: {noteName}, サブフォルダ: {subFolder ?? "なし"}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                var userPath = GetUserPath(uid, subFolder);
                string fullPath;

                // JSONファイルの場合は直接ファイルとして保存
                if (noteName.EndsWith(".json"))
                {
                    fullPath = $"{userPath}/{noteName}";
                }
                else
                {
                    fullPath = $"{userPath}/{noteName}/cards.txt";
                }

                var blobClient = containerClient.GetBlobClient(fullPath);

                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
                await blobClient.UploadAsync(stream, overwrite: true);
                Debug.WriteLine($"ノートの保存完了 - サイズ: {content.Length} バイト, パス: {fullPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ノートの保存中にエラー: {ex.Message}");
                throw;
            }
        }

        public async Task UploadImageAsync(string uid, string imageName, string base64Content, string subFolder = null)
        {
            await EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"画像のアップロード開始 - UID: {uid}, 画像名: {imageName}, サブフォルダ: {subFolder ?? "なし"}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                var userPath = GetUserPath(uid, subFolder);
                var fullPath = $"{userPath}/{imageName}";

                var blobClient = containerClient.GetBlobClient(fullPath);

                // Base64文字列をバイト配列に変換してアップロード
                var imageBytes = Convert.FromBase64String(base64Content);
                using var stream = new MemoryStream(imageBytes);
                await blobClient.UploadAsync(stream, overwrite: true);
                Debug.WriteLine($"画像のアップロード完了 - サイズ: {imageBytes.Length} バイト, パス: {fullPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"画像のアップロード中にエラー: {ex.Message}");
                throw;
            }
        }

        public async Task UploadImageBinaryAsync(string uid, string imageName, byte[] imageBytes, string subFolder = null)
        {
            await EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"画像のバイナリアップロード開始 - UID: {uid}, 画像名: {imageName}, サブフォルダ: {subFolder ?? "なし"}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                var userPath = GetUserPath(uid, subFolder);
                var fullPath = $"{userPath}/{imageName}";

                var blobClient = containerClient.GetBlobClient(fullPath);

                using var stream = new MemoryStream(imageBytes);
                await blobClient.UploadAsync(stream, overwrite: true);
                Debug.WriteLine($"画像のバイナリアップロード完了 - サイズ: {imageBytes.Length} バイト, パス: {fullPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"画像のバイナリアップロード中にエラー: {ex.Message}");
                throw;
            }
        }

        public async Task<List<string>> GetSubFoldersAsync(string uid)
        {
            await EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"サブフォルダ一覧の取得開始 - UID: {uid}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                var folders = new HashSet<string>();

                await foreach (var blob in containerClient.GetBlobsAsync(prefix: $"{uid}/"))
                {
                    Debug.WriteLine($"見つかったBlob: {blob.Name}");
                    
                    // cards.txtのパスから最初のディレクトリのみを取得
                    if (blob.Name.EndsWith("/cards.txt"))
                    {
                        var parts = blob.Name.Split('/');
                        if (parts.Length >= 3)
                        {
                            // UIDを除外し、最初のディレクトリのみを取得
                            var subFolder = parts[1];
                            if (!string.IsNullOrEmpty(subFolder))
                            {
                                folders.Add(subFolder);
                            }
                        }
                    }
                }

                Debug.WriteLine($"取得したサブフォルダ数: {folders.Count}");
                return folders.ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"サブフォルダ一覧の取得中にエラー: {ex.Message}");
                throw;
            }
        }

        public async Task CreateLocalNoteAsync(string uid, string noteName, string subFolder = null)
        {
            try
            {
                Debug.WriteLine($"ローカルノートの作成開始 - UID: {uid}, ノート名: {noteName}, サブフォルダ: {subFolder ?? "なし"}");
                
                // サーバーからノートの内容を取得
                var content = await GetNoteContentAsync(uid, noteName, subFolder);
                if (content == null)
                {
                    Debug.WriteLine($"サーバーにノートが存在しないため、作成をスキップします: {noteName}");
                    return;
                }

                // ローカルの保存先パスを構築
                var localPath = FolderPath;
                if (subFolder != null)
                {
                    localPath = Path.Combine(localPath, subFolder);
                }
                localPath = Path.Combine(localPath, noteName);

                // サブフォルダが存在しない場合は作成
                if (!Directory.Exists(localPath))
                {
                    Directory.CreateDirectory(localPath);
                    Debug.WriteLine($"サブフォルダを作成しました: {localPath}");
                }

                // ノートファイルのパス（サブフォルダを含めたパスで保存）
                var notePath = Path.Combine(localPath, "cards.txt");
                File.WriteAllText(notePath, content);
                Debug.WriteLine($"ローカルノートを作成しました: {notePath}");

                // cards.txtの場合、関連するJSONファイルも取得
                var jsonFiles = await GetNoteListAsync(uid, $"{subFolder}/{noteName}/cards");
                foreach (var jsonFile in jsonFiles)
                {
                    var jsonContent = await GetNoteContentAsync(uid, jsonFile, $"{subFolder}/{noteName}/cards");
                    if (jsonContent != null)
                    {
                        var cardsPath = Path.Combine(localPath, "cards");
                        if (!Directory.Exists(cardsPath))
                        {
                            Directory.CreateDirectory(cardsPath);
                        }
                        var jsonPath = Path.Combine(cardsPath, jsonFile);
                        File.WriteAllText(jsonPath, jsonContent);
                        Debug.WriteLine($"JSONファイルを作成しました: {jsonPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ローカルノートの作成中にエラー: {ex.Message}");
                throw;
            }
        }
    }
} 