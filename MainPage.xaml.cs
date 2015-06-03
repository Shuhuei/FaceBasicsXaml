//------------------------------------------------------------------------------
// <copyright file="MainPage.xaml.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.Resources;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using WindowsPreview.Kinect;
using Microsoft.Kinect.Face;

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
//using System.Windows.Media;
using Microsoft.Kinect;
using Microsoft.Kinect.Face;


namespace Microsoft.Samples.Kinect.FaceBasics
{
    /// <summary>
    /// MainPage のロジックの部分です。
    /// </summary>
    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        /// <summary>
        /// 深度データを、バイトデータに変換する際に使用します。
        /// </summary>
        private const int MapDepthToByte = 8000 / 256;

#if WIN81ORLATER
        private ResourceLoader resourceLoader = ResourceLoader.GetForCurrentView("Resources");
#else
        private ResourceLoader resourceLoader = new ResourceLoader("Resources");
#endif


        /// <summary>
        /// ビットマップ形式のデータを格納する際、インデックスに使用します。
        /// </summary>
        private readonly int cbytesPerPixel = 4;

        /// <summary>
        ///  Kinect センサー本体を扱うためのフィールドです。
        /// </summary>
        private KinectSensor kinectSensor = null;

        /// <summary>
        /// 深度センサーから届くフレームを扱うためのフィールドです。
        /// </summary>
        private DepthFrameReader depthFrameReader = null;

        /// <summary>
        /// ビットマップ形式のデータを画面上に表示させるためのフィールドです。
        /// </summary>
        private WriteableBitmap bitmap = null;

        /// <summary>
        /// Kinect センサーが取得した、フレームの深度データが格納されるフィールドです。
        /// </summary>
        private ushort[] depthFrameData = null;

        /// <summary>
        /// 深度フレームの情報を RGB に変換されたデータが格納されるフィールドです。
        /// </summary>
        private byte[] depthPixels = null;

        /// <summary>
        /// アプリが適切に動作しているものかを表示するための情報を扱います。
        /// </summary>
        private string statusText = null;

        //ここから Sheda Added

        public MainPage()
        {
            // Kinect センサー V2 ののオブジェクトを取得します。
            this.kinectSensor = KinectSensor.GetDefault();

            // 深度フレームに関する情報が格納されたオブジェクトを取得します。
            FrameDescription depthFrameDescription = this.kinectSensor.DepthFrameSource.FrameDescription;

            // 深度フレームを読み込むための Reader を開きます。
            this.depthFrameReader = this.kinectSensor.DepthFrameSource.OpenReader();

            // 深度情報が格納されたフレームが到着したことを示すイベント "FrameArrived" が発生した際に
            // "Reader_DepthFrameArrived" の処理が実行されるようにイベントハンドラを登録します。
            this.depthFrameReader.FrameArrived += this.Reader_DepthFrameArrived;

            // 512 × 424 サイズの配列を定義します。
            this.depthFrameData = new ushort[depthFrameDescription.Width * depthFrameDescription.Height];
            this.depthPixels = new byte[depthFrameDescription.Width * depthFrameDescription.Height * this.cbytesPerPixel];

            // ディスプレイに出力するための ビットマップデータを出力します。
            this.bitmap = new WriteableBitmap(depthFrameDescription.Width, depthFrameDescription.Height);//, 96.0, 96.0, PixelFormats.Bgr32, null);

            // Kinect センサーについて (USB 接続が切れてしまった等) 変化した場合に
            // "Sensor_IsAvailableChanged" の処理が実行されるようにイベントハンドラを登録します。
            this.kinectSensor.IsAvailableChanged += this.Sensor_IsAvailableChanged;


            // Kinect Sensor の処理を開始します。
            this.kinectSensor.Open();

            // Kinect センサーが適切に動作しているものかを表示します。(アプリの画面左下に表示されます。)
            this.StatusText = this.kinectSensor.IsAvailable ? resourceLoader.GetString("RunningStatusText")
                                                            : resourceLoader.GetString("NoSensorStatusText");

            // View Model として、扱えるように DataContext に MainPage クラスを指定します。
            this.DataContext = this;

            // アプリの起動に必要な初期化処理を実行します。
            this.InitializeComponent();
        }

        /// <summary>
        /// INotifyPropertyChangedPropertyChanged を用いて、プロパティの変更を画面コントロールに通知します。 
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// 現在の Kinect センサーの状態を表示するためのプロパティです。
        /// </summary>
        public string StatusText
        {
            get
            {
                return this.statusText;
            }

            set
            {
                if (this.statusText != value)
                {
                    this.statusText = value;
                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new PropertyChangedEventArgs("StatusText"));
                    }
                }
            }
        }

        /// <summary>
        /// MainPage のシャットダウンの処理です。
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (this.depthFrameReader != null)
            {
                // DepthFrameReader の オブジェクトを解放します。
                this.depthFrameReader.Dispose();
                this.depthFrameReader = null;
            }

            if (this.kinectSensor != null)
            {
                // Kinect センサーの処理を終了します。
                this.kinectSensor.Close();
                this.kinectSensor = null;
            }
        }

        /// <summary>
        /// 深度センサーからフレームが到着した際に呼びされるイベントハンドラーです。
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void Reader_DepthFrameArrived(object sender, DepthFrameArrivedEventArgs e)
        {
            ushort minDepth = 0;
            ushort maxDepth = 0;

            bool depthFrameProcessed = false;
            
            using (DepthFrame depthFrame = e.FrameReference.AcquireFrame())
            {
                // 到着したフレームが存在する場合に処理を継続します。
                if (depthFrame != null)
                {
                    // 深度フレームを読み込むための Reader を開きます。                    
                    FrameDescription depthFrameDescription = depthFrame.FrameDescription;

                    // 到着した深度フレームのサイズ縦横のサイズと、用意した depthFrameDescription が持つ縦横比が一致しているか確認します。
                    if (((depthFrameDescription.Width * depthFrameDescription.Height) == this.depthFrameData.Length) &&
                        (depthFrameDescription.Width == this.bitmap.PixelWidth) && (depthFrameDescription.Height == this.bitmap.PixelHeight))
                    {

                        // 深度フレームのデータを、コピーし、配列に格納します。
                        depthFrame.CopyFrameDataToArray(this.depthFrameData);

                        // 深度センサーの観測可能範囲は、0.5 - 4.5 mです。
                        // そのため、MinDepth は、500 (mm)
                        // MaxValue は、65535 (mm)としています。
                        // MaxValue に対して、4.5 m より十分大きな値が格納されているのでは、Kinect の深度取得可能範囲を
                        // 包含できるようにするためです。

                        minDepth = depthFrame.DepthMinReliableDistance;
                        maxDepth = ushort.MaxValue;

                        // もし Kinect センサーで定義されている、深度情報の取得可能な区間を格納したい場合は下記のコメントを外して下さい。
                        //// maxDepth = depthFrame.DepthMaxReliableDistance
                        
                        depthFrameProcessed = true;
                    }
                }
            }

            // 深度情報を元に描画処理を実行します。
            if (depthFrameProcessed)
            {
                //深度情報を、RGB 値に変換します。
                ConvertDepthData(minDepth, maxDepth);

                //変換したデータを画面に描画します。
                RenderDepthPixels(this.depthPixels);
            }
        }

        /// <summary>
        /// フレーム内の深度情報を RGB 値に変換します。
        /// </summary>
        /// <param name="frame"></param>
        private void ConvertDepthData(ushort minDepth, ushort maxDepth)
        {
            int colorPixelIndex = 0;

            // フレーム内に存在する 512 * 424 ピクセルの各深度データに対して、RGB 値に変換する処理を実行します。 
            for (int i = 0; i < this.depthFrameData.Length; ++i)
            {
                ushort depth = this.depthFrameData[i];

                // 深度情報を、RGB 値に変換できるように補正処理します。
                byte intensity = (byte)(depth >= minDepth && depth <= maxDepth ? (depth / MapDepthToByte) : 0);

                //上記で求めた、intensity の値を、下記に RGB 値として入力します。

                this.depthPixels[colorPixelIndex++] = intensity;

                this.depthPixels[colorPixelIndex++] = intensity;

                this.depthPixels[colorPixelIndex++] = intensity;

                this.depthPixels[colorPixelIndex++] = 255;
            }
        }

        /// <summary>
        /// 画面への描画処理を実行します。
        /// </summary>
        /// <param name="pixels">pixel data</param>
        private void RenderDepthPixels(byte[] pixels)
        {
            pixels.CopyTo(this.bitmap.PixelBuffer);
            this.bitmap.Invalidate();
            theImage.Source = this.bitmap;
        }

        /// <summary>
        /// Kinect センサーの状態が変わったときに呼び出されます。
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void Sensor_IsAvailableChanged(object sender, IsAvailableChangedEventArgs e)
        {
            this.StatusText = this.kinectSensor.IsAvailable ? resourceLoader.GetString("RunningStatusText")
                                                            : resourceLoader.GetString("SensorNotAvailableStatusText");
        }
    }
}
