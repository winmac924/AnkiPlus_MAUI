using Microsoft.Maui.Controls;
using SkiaSharp.Views.Maui;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using SkiaSharp.Views.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using System.Text.Json;
using AnkiPlus_MAUI.Models;
using AnkiPlus_MAUI.Services;
using System.Reflection;

namespace AnkiPlus_MAUI
{
    public partial class Add : ContentPage
    {
        private CardManager _cardManager;

        public Add(string cardsPath, string tempPath, string cardId = null)
        {
            try
            {
                Debug.WriteLine($"Add.xaml.cs コンストラクタ開始");
                Debug.WriteLine($"cardsPath: {cardsPath}");
                Debug.WriteLine($"tempPath: {tempPath}");
                Debug.WriteLine($"cardId: {cardId}");

                InitializeComponent();
                Debug.WriteLine("InitializeComponent完了");

                // CardManagerを初期化
                _cardManager = new CardManager(cardsPath, tempPath, cardId);
                Debug.WriteLine("CardManager初期化完了");

                // CardManagerを使用してUIを初期化
                _cardManager.InitializeCardUI(CardContainer, includePageImageButtons: false);
                Debug.WriteLine("CardUI初期化完了");

                Debug.WriteLine("Add.xaml.cs コンストラクタ完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Add.xaml.cs コンストラクタでエラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                throw;
            }
        }

        // CardManagerに移行済み - 削除
    }
}