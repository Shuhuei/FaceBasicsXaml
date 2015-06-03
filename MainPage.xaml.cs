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
using System.Diagnostics;
using System.Windows;


namespace Microsoft.Samples.Kinect.FaceBasics
{
    /// <summary>
    /// MainPage のロジックの部分です。
    /// </summary>
    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {

#if WIN81ORLATER
        private ResourceLoader resourceLoader = ResourceLoader.GetForCurrentView("Resources");
#else
        private ResourceLoader resourceLoader = new ResourceLoader("Resources");
#endif


       

        /// <summary>
        ///  Kinect センサー本体を扱うためのフィールドです。
        /// </summary>
        private KinectSensor kinectSensor = null;


        /// <summary>
        /// アプリが適切に動作しているものかを表示するための情報を扱います。
        /// </summary>
        private string statusText = null;

        //ここから Sheda Added
        /// <summary>
        /// Face rotation display angle increment in degrees
        /// </summary>
        private const double FaceRotationIncrementInDegrees = 5.0;

        /// <summary>
        /// Reader for body frames
        /// </summary>
        private BodyFrameReader bodyFrameReader = null;

        /// <summary>
        /// Array to store bodies
        /// </summary>
        private Body[] bodies = null;

        /// <summary>
        /// Number of bodies tracked
        /// </summary>
        private int bodyCount;

        /// <summary>
        /// Face frame source
        /// </summary>
        private FaceFrameSource faceFrameSource = null;
        /// <summary>
        /// Face frame reader
        /// </summary>
        private FaceFrameReader faceFrameReader = null;


        /// <summary>
        /// Storage for face frame results
        /// </summary>
        private FaceFrameResult faceFrameResult = null;

        /// <summary>
        /// Width of display (color space)
        /// </summary>
        private int displayWidth;

        /// <summary>
        /// Height of display (color space)
        /// </summary>
        private int displayHeight;


        public MainPage() 
        {

            // one sensor is currently supported
            this.kinectSensor = KinectSensor.GetDefault();

            // get the color frame details
            FrameDescription frameDescription = this.kinectSensor.ColorFrameSource.FrameDescription;

            // set the display specifics
            this.displayWidth = frameDescription.Width;
            this.displayHeight = frameDescription.Height;

            // open the reader for the body frames
            this.bodyFrameReader = this.kinectSensor.BodyFrameSource.OpenReader();

            // wire handler for body frame arrival
            this.bodyFrameReader.FrameArrived += this.Reader_BodyFrameArrived;

            // set the maximum number of bodies that would be tracked by Kinect
            this.bodyCount = this.kinectSensor.BodyFrameSource.BodyCount;

            // allocate storage to store body objects
            this.bodies = new Body[this.bodyCount];

            // specify the required face frame results
            FaceFrameFeatures faceFrameFeatures =
                FaceFrameFeatures.BoundingBoxInColorSpace
                | FaceFrameFeatures.PointsInColorSpace
                | FaceFrameFeatures.RotationOrientation
                | FaceFrameFeatures.FaceEngagement
                | FaceFrameFeatures.Glasses
                | FaceFrameFeatures.Happy
                | FaceFrameFeatures.LeftEyeClosed
                | FaceFrameFeatures.RightEyeClosed
                | FaceFrameFeatures.LookingAway
                | FaceFrameFeatures.MouthMoved
                | FaceFrameFeatures.MouthOpen;

            // create a face frame source + reader to track each face in the FOV
            this.faceFrameSource = new FaceFrameSource(this.kinectSensor, 0, faceFrameFeatures);
            this.faceFrameReader = this.faceFrameSource.OpenReader();

            // open the sensor
            this.kinectSensor.Open();

            // initialize the components (controls) of the window
            this.InitializeComponent();

        }

        /// <summary>
        /// INotifyPropertyChangedPropertyChanged を用いて、プロパティの変更を画面コントロールに通知します。 
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Converts rotation quaternion to Euler angles 
        /// And then maps them to a specified range of values to control the refresh rate
        /// </summary>
        /// <param name="rotQuaternion">face rotation quaternion</param>
        /// <param name="pitch">rotation about the X-axis</param>
        /// <param name="yaw">rotation about the Y-axis</param>
        /// <param name="roll">rotation about the Z-axis</param>
        private static void ExtractFaceRotationInDegrees(Vector4 rotQuaternion, out int pitch, out int yaw, out int roll)
        {
            double x = rotQuaternion.X;
            double y = rotQuaternion.Y;
            double z = rotQuaternion.Z;
            double w = rotQuaternion.W;

            // convert face rotation quaternion to Euler angles in degrees
            double yawD, pitchD, rollD;
            pitchD = Math.Atan2(2 * ((y * z) + (w * x)), (w * w) - (x * x) - (y * y) + (z * z)) / Math.PI * 180.0;
            yawD = Math.Asin(2 * ((w * y) - (x * z))) / Math.PI * 180.0;
            rollD = Math.Atan2(2 * ((x * y) + (w * z)), (w * w) + (x * x) - (y * y) - (z * z)) / Math.PI * 180.0;

            // clamp the values to a multiple of the specified increment to control the refresh rate
            double increment = FaceRotationIncrementInDegrees;
            pitch = (int)(Math.Floor((pitchD + ((increment / 2.0) * (pitchD > 0 ? 1.0 : -1.0))) / increment) * increment);
            yaw = (int)(Math.Floor((yawD + ((increment / 2.0) * (yawD > 0 ? 1.0 : -1.0))) / increment) * increment);
            roll = (int)(Math.Floor((rollD + ((increment / 2.0) * (rollD > 0 ? 1.0 : -1.0))) / increment) * increment);
        }



        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            this.faceFrameReader.FrameArrived += this.Reader_FaceFrameArrived;

            if (this.bodyFrameReader != null)
            {
                // wire handler for body frame arrival
                this.bodyFrameReader.FrameArrived += this.Reader_BodyFrameArrived;
            }
        }

        /// <summary>
        /// Handles the face frame data arriving from the sensor
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void Reader_FaceFrameArrived(object sender, FaceFrameArrivedEventArgs e)
        {
            using (FaceFrame faceFrame = e.FrameReference.AcquireFrame())
            {
                if (faceFrame != null)
                {
                    // check if this face frame has valid face frame results
                    if (this.ValidateFaceBoxAndPoints(faceFrame.FaceFrameResult))
                    {
                        // store this face frame result to draw later
                        this.faceFrameResult = faceFrame.FaceFrameResult;
                    }
                    else
                    {
                        // indicates that the latest face frame result from this reader is invalid
                        this.faceFrameResult = null;
                    }
                }
            }
        }

        /// <summary>
        /// Returns the index of the face frame source
        /// </summary>
        /// <param name="faceFrameSource">the face frame source</param>
        /// <returns>the index of the face source in the face source array</returns>

        /// <summary>
        /// Handles the body frame data arriving from the sensor
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void Reader_BodyFrameArrived(object sender, BodyFrameArrivedEventArgs e)
        {
            using (var bodyFrame = e.FrameReference.AcquireFrame())
            {
                if (bodyFrame != null)
                {
                    // update body data
                    bodyFrame.GetAndRefreshBodyData(this.bodies);

                    // check if a valid face is tracked in this face source
                    if (this.faceFrameSource.IsTrackingIdValid)
                    {
                        // check if we have valid face frame results
                        if (this.faceFrameResult != null)
                        {
                            // draw face frame results
                            this.DrawFaceFrameResults(this.faceFrameResult);
                        }
                    }
                    else
                    {
                        // check if the corresponding body is tracked 
                        for (int i = 0; i < this.bodyCount; i++)
                        {
                            if (this.bodies[i].IsTracked)
                            {
                                // update the face frame source to track this body
                                this.faceFrameSource.TrackingId = this.bodies[i].TrackingId;
                                break;
                            }
                        }
                    }

                }
            }
        }

        /// <summary>
        /// Draws face frame results
        /// </summary>
        /// <param name="faceIndex">the index of the face frame corresponding to a specific body in the FOV</param>
        /// <param name="faceResult">container of all face frame results</param>
        /// <param name="drawingContext">drawing context to render to</param>
        private void DrawFaceFrameResults(FaceFrameResult faceResult)
        {
            Debug.WriteLine("");
            // extract each face property information and store it in faceText
            if (faceResult.FaceProperties != null)
            {
                foreach (var item in faceResult.FaceProperties)
                {
                    // consider a "maybe" as a "no" to restrict 
                    // the detection result refresh rate
                    if (item.Value == DetectionResult.Maybe)
                    {
                        Debug.WriteLine(item.Key.ToString() + " : " + DetectionResult.No);
                    }
                    else
                    {
                        Debug.WriteLine(item.Key.ToString() + " : " + item.Value.ToString());
                    }
                }
            }

            // extract face rotation in degrees as Euler angles
            if (!faceResult.FaceRotationQuaternion.Equals(null))
            {
                int pitch, yaw, roll;
                ExtractFaceRotationInDegrees(faceResult.FaceRotationQuaternion, out pitch, out yaw, out roll);
                Debug.WriteLine("FaceYaw : " + yaw);
                Debug.WriteLine("FacePitch : " + pitch);
                Debug.WriteLine("FacenRoll : " + roll);
            }
        }

        /// <summary>
        /// Validates face bounding box and face points to be within screen space
        /// </summary>
        /// <param name="faceResult">the face frame result containing face box and points</param>
        /// <returns>success or failure</returns>
        private bool ValidateFaceBoxAndPoints(FaceFrameResult faceResult)
        {
            bool isFaceValid = faceResult != null;

            if (isFaceValid)
            {
                var faceBox = faceResult.FaceBoundingBoxInColorSpace;
                if (!faceBox.Equals(null) )
                {
                    // check if we have a valid rectangle within the bounds of the screen space
                    isFaceValid = (faceBox.Right - faceBox.Left) > 0 &&
                                  (faceBox.Bottom - faceBox.Top) > 0 &&
                                  faceBox.Right <= this.displayWidth &&
                                  faceBox.Bottom <= this.displayHeight;

                    if (isFaceValid)
                    {
                        var facePoints = faceResult.FacePointsInColorSpace;
                        if (facePoints != null)
                        {
                            foreach (/*PointF*/ var pointF in facePoints.Values)
                            {
                                // check if we have a valid face point within the bounds of the screen space
                                bool isFacePointValid = pointF.X > 0.0f &&
                                                        pointF.Y > 0.0f &&
                                                        pointF.X < this.displayWidth &&
                                                        pointF.Y < this.displayHeight;

                                if (!isFacePointValid)
                                {
                                    isFaceValid = false;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            return isFaceValid;
        }


   

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
