using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using BitmapEncoder = Windows.Graphics.Imaging.BitmapEncoder;

namespace InfraredMediaFrameSample
{
    public partial class MainWindow : Window
    {
        private bool _running = false;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += Start;
        }


        private async void Start(object sender, RoutedEventArgs e)
        {
            // デバイスで現在利用可能な MediaFrameSourceGroup のリストを取得する
            var frameSourceGroups = await MediaFrameSourceGroup.FindAllAsync();

            // IR データを生成する FrameSourceGroups を選択
            var selectedGroup = frameSourceGroups.FirstOrDefault(group => group.SourceInfos.Any(info => info.SourceKind == MediaFrameSourceKind.Infrared));
            if (selectedGroup == null)
            {
                Debug.WriteLine("IR カメラが見つかりませんでした");
                return;
            }

            var mediaCapture = new MediaCapture();
            try
            {
                // MediaCapture に選択したソースを設定して初期化
                await mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings()
                {
                    SourceGroup = selectedGroup,
                    SharingMode = MediaCaptureSharingMode.ExclusiveControl,
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                    StreamingCaptureMode = StreamingCaptureMode.Video
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine("MediaCapture の初期化に失敗しました: " + ex.Message);
                return;
            }

            // 選択したソースから MediaFrameReader を作成
            var mediaFrameReader = await mediaCapture.CreateFrameReaderAsync(mediaCapture.FrameSources[selectedGroup.SourceInfos[0].Id]);

            // MediaFrameReader の FrameArrived イベントハンドラに処理を登録
            mediaFrameReader.FrameArrived += MediaFrameReader_FrameArrived;

            // MediaFrameReader の開始
            await mediaFrameReader.StartAsync();
        }


        /// <summary>
        /// MediaFrameReader にフレームが到着した時の処理
        /// </summary>
        private void MediaFrameReader_FrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            // sender から最新フレームへの参照を取得
            using var latestFrameReference = sender.TryAcquireLatestFrame();

            // 最新フレームのビットマップ
            var softwareBitmap = latestFrameReference.VideoMediaFrame.SoftwareBitmap;

            // WPF の Image コントロールで表示できるよう、BGRA8 のアルファ乗算済みに変換する
            if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                softwareBitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
            {
                softwareBitmap = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            }

            // UI スレッドで画像を更新する
            CameraImage.Dispatcher.BeginInvoke(async () =>
            {
                // 同時実行させない
                if (_running) return;
                _running = true;

                // WPF の Image コントロールで表示できるよう、SoftwareBitmap から BitmapImage に変換する
                CameraImage.Source = await ConvertSoftwareBitmap2BitmapImage(softwareBitmap);

                _running = false;
            });
        }

        /// <summary>
        /// インメモリで SoftwareBitmap から BitmapImage に変換する
        /// </summary>
        /// <param name="src">変換元</param>
        /// <returns>変換結果</returns>
        private static async Task<BitmapImage> ConvertSoftwareBitmap2BitmapImage(SoftwareBitmap src)
        {
            // インメモリストリームに SoftwareBitmap をセット
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.BmpEncoderId, stream);
            encoder.SetSoftwareBitmap(src);
            await encoder.FlushAsync();

            // インメモリストリームから BitmapImage を作成
            var result = new BitmapImage();
            result.BeginInit();
            result.StreamSource = stream.AsStream();
            result.CacheOption = BitmapCacheOption.OnLoad;
            result.EndInit();
            result.Freeze();

            return result;
        }
    }
}