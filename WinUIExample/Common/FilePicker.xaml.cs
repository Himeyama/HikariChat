
using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Windows.Storage;
using WinRT.Interop;
using System.Collections.Generic;
using System.IO;
using Windows.Storage.Provider;

namespace Common;

public sealed partial class FilePicker : Page {
    /// <summary>
    /// ユーザーがファイルを選択するためのファイルオープンピッカーを表示します。
    /// </summary>
    /// <param name="mainWindow">現在のアプリケーションのメインウィンドウを表すオブジェクト。</param>
    /// <returns>選択されたファイルのパスを文字列として返します。ファイルが選択されなかった場合はnullを返します。</returns>
    public static async Task<string> Open(object mainWindow)
    {
        // ファイルオープンピッカーを作成
        FileOpenPicker openPicker = new();

        // 現在のWinUI 3ウィンドウのハンドル（HWND）を取得
        nint hWnd = WindowNative.GetWindowHandle(mainWindow);

        // ウィンドウハンドルでファイルピッカーを初期化
        InitializeWithWindow.Initialize(openPicker, hWnd);

        // ファイルピッカーのオプションを設定
        ConfigureFilePicker(openPicker);

        // ユーザーがファイルを選択するためのピッカーを表示
        StorageFile file = await openPicker.PickSingleFileAsync();
        
        // 選択されたファイルのパスを返す。選択されていない場合はnullを返す。
        return file?.Path;
    }

    static void ConfigureFilePicker(FileOpenPicker openPicker)
    {
        // ビューモードをサムネイルに設定
        openPicker.ViewMode = PickerViewMode.Thumbnail;
        // ファイルタイプフィルターを設定（すべてのファイルを選択可能）
        openPicker.FileTypeFilter.Add("*");
    }

    /// <summary>
    /// ユーザーが選択したファイルに指定されたテキストを保存するメソッドです。
    /// ファイル保存ダイアログを表示し、ユーザーが選択した場合は、そのファイルにテキストを書き込みます。
    /// 操作の結果に応じて、適切な整数を返します。
    /// </summary>
    /// <param name="mainWindow">現在のウィンドウのオブジェクト</param>
    /// <param name="text">保存するテキスト</param>
    /// <returns>
    /// 0: 保存が完了した場合
    /// 1: 保存が完了し、ファイル名が変更された場合
    /// 2: 保存が完了しなかった場合
    /// 3: ユーザーがファイル選択をキャンセルした場合
    /// </returns>
    public static async Task<int> Save(object mainWindow, string text) {
        // ファイルピッカーを作成する
        FileSavePicker savePicker = new();

        // 現在のWinUI 3ウィンドウのウィンドウハンドル（HWND）を取得する
        nint hWnd = WindowNative.GetWindowHandle(mainWindow);

        // ウィンドウハンドル（HWND）でファイルピッカーを初期化する
        InitializeWithWindow.Initialize(savePicker, hWnd);

        // ファイルピッカーのオプションを設定する
        savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        // ユーザーがファイルを保存する際に選択できるファイルタイプのドロップダウン
        savePicker.FileTypeChoices.Add("Plain Text", new List<string>() { ".txt" });

        // ユーザーがファイルを選択するためにピッカーを開く
        StorageFile file = await savePicker.PickSaveFileAsync();
        if (file != null) {
            // 変更を行っている間、リモートバージョンのファイルへの更新を防ぐ
            CachedFileManager.DeferUpdates(file);
            
            using (Stream stream = await file.OpenStreamForWriteAsync()) {
                using var tw = new StreamWriter(stream);
                tw.WriteLine(text); // テキストをファイルに書き込む
            }

            // Windows に、ファイルの変更が完了したことを知らせ、他のアプリがリモートバージョンの更新を行えるようにする
            // 更新の完了には、Windowsからユーザーの入力を求められる場合がある
            FileUpdateStatus status = await CachedFileManager.CompleteUpdatesAsync(file);
            if (status == FileUpdateStatus.Complete) {
                return 0; // 完了した場合
            } else if (status == FileUpdateStatus.CompleteAndRenamed) {
                return 1; // 完了し、名前が変更された場合
            } else {
                return 2; // 更新が完了しなかった場合
            }
        } else {
            return 3; // ユーザーがファイルの選択をキャンセルした場合
        }
    }
}