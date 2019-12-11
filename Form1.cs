using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Lab6
{
    public partial class Form1 : Form
    {
        VideoCapture _capture;
        Thread _captureThread;
        //arduino stuff
        //byte[] buffer = new byte[1];
        SerialPort arduinoSerial = new SerialPort();
        bool enableCoordinateSending = true;
        Thread serialMonitoringThread;

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // create the capture object and processing thread
            _capture = new VideoCapture(1);
            _captureThread = new Thread(ProcessImage);
            _captureThread.Start();
            try
            {
                arduinoSerial.PortName = "COM6";
                arduinoSerial.BaudRate = 115200;
                arduinoSerial.Open();
                serialMonitoringThread = new Thread(MonitorSerialData);
                serialMonitoringThread.Start();
                textBox1.Text = "130"; //x
                textBox2.Text = "224"; //y
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error Initializing COM port");
                Close();
            }
        }

        private void ProcessImage()
        {
            while (_capture.IsOpened)
            {
                // frame maintenance
                Mat workingImage = _capture.QueryFrame();
                // resize to PictureBox aspect ratio
                int newHeight = (workingImage.Size.Height * pictureBox1.Size.Width) / workingImage.Size.Width;
                Size newSize = new Size(pictureBox1.Size.Width, newHeight);
                CvInvoke.Resize(workingImage, workingImage, newSize);
                // as a test for comparison, create a copy of the image with a binary filter:
                var binaryImage = workingImage.ToImage<Gray, byte>().ThresholdBinary(new Gray(125), new
                Gray(255)).Mat;
                // Sample for gaussian blur:
                var blurredImage = new Mat();
                var cannyImage = new Mat();
                var decoratedImage = new Mat();
                CvInvoke.GaussianBlur(workingImage, blurredImage, new Size(9, 9), 0);
                // convert to B/W
                CvInvoke.CvtColor(blurredImage, blurredImage, typeof(Bgr), typeof(Gray));
                // apply canny:
                // NOTE: Canny function can frequently create duplicate lines on the same shape
                // depending on blur amount and threshold values, some tweaking might be needed.
                // You might also find that not using Canny and instead using FindContours on
                // a binary-threshold image is more accurate.
                CvInvoke.Canny(blurredImage, cannyImage, 150, 255);
                // make a copy of the canny image, convert it to color for decorating:
                CvInvoke.CvtColor(cannyImage, decoratedImage, typeof(Gray), typeof(Bgr));
                // find contours:
                //Mat sourceFrameWithArt = workingImage.Clone();
                using (VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint())
                {
                    string shape = " ";
                    // Build list of contours
                    CvInvoke.FindContours(cannyImage, contours, null, RetrType.List, ChainApproxMethod.ChainApproxSimple);
                    for (int i = 0; i < contours.Size; i++)
                    {
                        VectorOfPoint contour = contours[i];
                        CvInvoke.Polylines(decoratedImage, contour, true, new Bgr(Color.Black).MCvScalar); //*****************This Line hides the uneeded contours***************
                        using (VectorOfPoint approxContour = new VectorOfPoint())
                        {
                            CvInvoke.ApproxPolyDP(contour, approxContour, CvInvoke.ArcLength(contour, true) * 0.05, true);
                            if (CvInvoke.ContourArea(approxContour, false) > 250) //only consider contours with area greater than 250
                            {
                                if (approxContour.Size == 3)
                                {
                                    shape = "Triangle";
                                    Invoke(new Action(() =>
                                    {
                                        label3.Text = "Triangle";
                                        label8.Text = $"0";
                                        CvInvoke.Polylines(decoratedImage, contour, true, new Bgr(Color.Green).MCvScalar);
                                    }));
                                }
                                if (approxContour.Size == 4)
                                {
                                    shape = "Rectangle";
                                    Invoke(new Action(() =>
                                    {
                                        label3.Text = "Rectangle";
                                        label8.Text = $"1";
                                        CvInvoke.Polylines(decoratedImage, contour, true, new Bgr(Color.Red).MCvScalar);
                                    }));
                                }
                                Invoke(new Action(() =>
                                {
                                    label2.Text = $"There are {approxContour.Size} corners detected";
                                }));

                            }
                            Rectangle boundingBox = CvInvoke.BoundingRectangle(contours[i]);
                            //CvInvoke.Polylines(decoratedImage, contour, true, new Bgr(Color.Green).MCvScalar);
                            MarkDetectedObject(workingImage, contours[i], boundingBox, CvInvoke.ContourArea(contour), shape);
                            Point center = new Point(boundingBox.X + boundingBox.Width / 2, boundingBox.Y + boundingBox.Height / 2);
                            Invoke(new Action(() =>
                            {
                                label4.Text = $"Position: {center.X}, {center.Y}"; //coords that get sent to the arduino
                                textBox1.Text = $"{center.X}";
                                textBox2.Text = $"{center.Y}";
                            }));
                            Thread.Sleep(50);
                        }

                    }
                    Invoke(new Action(() =>
                    {
                        label1.Text = $"There are {contours.Size} contours detected"; //# of total contours
                    }));

                }
                // output images:
                pictureBox1.Image = workingImage.Bitmap;
                pictureBox2.Image = decoratedImage.Bitmap;
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (!enableCoordinateSending)
            {
                MessageBox.Show("Temporarily locked...");
                return;
            }
            int x = -1;
            int y = -1;
            int data = 1;
            if(label8.Text == "1")
            {
                data = 1;
            }
            else
            {
                data = 0;
            }
            if (int.TryParse(textBox1.Text, out x) && int.TryParse(textBox2.Text, out y))
            {
                byte[] buffer = new byte[5] {

                    Encoding.ASCII.GetBytes("<")[0],
                    Convert.ToByte(x),
                    Convert.ToByte(y),
                    Convert.ToByte(data),
                    Encoding.ASCII.GetBytes(">")[0]
                };
                arduinoSerial.Write(buffer, 0, 4);
            }
            else
            {
                MessageBox.Show("X and Y values must be integers", "Unable to parse coordinates");
            }
        }

        private void MonitorSerialData()
        {
            while (true)
            {
                // block until \n character is received, extract command data
                string msg = arduinoSerial.ReadLine();
                // confirm the string has both < and > characters
                if (msg.IndexOf("<") == -1 || msg.IndexOf(">") == -1)
                {
                    continue;
                }
                // remove everything before the < character
                msg = msg.Substring(msg.IndexOf("<") + 1);
                // remove everything after the > character
                msg = msg.Remove(msg.IndexOf(">"));
                // if the resulting string is empty, disregard and move on
                if (msg.Length == 0)
                {
                    continue;
                }
                // parse the command
                if (msg.Substring(0, 1) == "S")
                {
                    // command is to suspend, toggle states accordingly:
                    ToggleFieldAvailability(msg.Substring(1, 1) == "1");
                }
                else if (msg.Substring(0, 1) == "P")
                {
                    // command is to display the point data, output to the text field:
                    Invoke(new Action(() =>
                    {
                        label6.Text = $"Returned Point Data: {msg.Substring(1)}";
                    }));
                }
            }
        }

        private void ToggleFieldAvailability(bool suspend)
        {
            Invoke(new Action(() =>
            {
                enableCoordinateSending = !suspend;
                label7.Text = $"State: {(suspend ? "Locked" : "Unlocked")}";
            }));
        }

        private static void MarkDetectedObject(Mat frame, VectorOfPoint contour, Rectangle boundingBox, double area, string shape)
        {
            // Drawing contour and box around it
            CvInvoke.Polylines(frame, contour, true, new Bgr(Color.Red).MCvScalar);
            CvInvoke.Rectangle(frame, boundingBox, new Bgr(Color.Red).MCvScalar);
            // Write information next to marked object
            Point center = new Point(boundingBox.X + boundingBox.Width / 2, boundingBox.Y + boundingBox.Height / 2);
            var info = new string[] {
                $"Area: {area}",
                $"Position: {center.X}, {center.Y}",
                $"Shape: {shape}"
                };
            WriteMultilineText(frame, info, new Point(center.X, boundingBox.Bottom + 12));
            CvInvoke.Circle(frame, center, 5, new Bgr(Color.Aqua).MCvScalar, 5);
        }

        private static void WriteMultilineText(Mat frame, string[] lines, Point origin)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                int y = i * 10 + origin.Y; // Moving down on each line
                CvInvoke.PutText(frame, lines[i], new Point(origin.X, y),
                FontFace.HersheyPlain, 0.8, new Bgr(Color.Red).MCvScalar);
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // terminate the image processing thread to avoid orphaned processes
            _captureThread.Abort();
            serialMonitoringThread.Abort();
        }
    }
}
