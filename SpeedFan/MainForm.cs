using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Imaging;
using OpenHardwareMonitor.Hardware;
using System.Linq;
using System.Drawing.Drawing2D;
using System.Net.NetworkInformation;
using System.Data;

namespace SpeedFan
{
    public static class Ext
    {
        public static T Clamp<T>(this T val, T min, T max) where T : IComparable<T>
        {
            if (val.CompareTo(min) < 0) return min;
            else if (val.CompareTo(max) > 0) return max;
            else return val;
        }
        public static void ShiftLeft<T>(List<T> lst, int shifts)
        {
            for (int i = 1; i < lst.Count; i++)
            {
                lst[i - 1] = lst[i];
            }
            lst[lst.Count - 1] = default(T);
        }
        public static Bitmap ResizeImage(Image image, int width, int height)
        {
            var destRect = new Rectangle(0, 0, width, height);
            var destImage = new Bitmap(width, height);

            destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);

            using (var graphics = Graphics.FromImage(destImage))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using (var wrapMode = new ImageAttributes())
                {
                    wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
                }
            }

            return destImage;
        }
    }

    public partial class MainForm : Form
    {
        public static class TemperatureGraph
        {
            public const int Columns = 100;
            public const int MinRows = 30;
            public const int MaxRows = 80;

            public static int Rows;
            const double BmpScale = 1.3f;

            public static int ColumnWidth;
            public static int RowHeight;

            public const int FontSize = 15;

            public const int TextWidth = 1000;
            public const int TextHeight = 15;

            public static Bitmap Bmp { get; set; } = new Bitmap((int)(Window.WindowWidth * BmpScale), (int)(Window.WindowHeight * BmpScale));

            public static void BlackenBitmap()
            {
                using (Graphics gfx = Graphics.FromImage(Bmp))
                {
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(0, 0, 0)))
                    {
                        gfx.FillRectangle(brush, 0, 0, Bmp.Width, Bmp.Height);
                    }
                }
            }
            public static Bitmap GetScaledBitmap()
            {
                return Ext.ResizeImage(Bmp, Window.WindowWidth, Window.WindowHeight);
            }
        }

        public static class Temperatures
        {
            public static int InitialMinMeasurement = 40;
            public static int InitialMaxMeasurement = 60;

            public static int CurrentMinMeasurement;
            public static int CurrentMaxMeasurement;

            public static int MinMaxTemperatureDelta;

            const int MaxTemperaturesCount = (int)(TemperatureGraph.Columns * 0.9);

            public static Queue<int> GpuTemps { get; } = new Queue<int>();
            public static int CurrentGpuTemp
            {
                get
                {
                    return GpuTemps.Last();
                }
            }
            public static void AddGpuTemp(int gpuTemp)
            {
                if (GpuTemps.Count() > MaxTemperaturesCount)
                {
                    //Ext.ShiftLeft(GpuTemps, 1);
                    GpuTemps.Dequeue();
                    GpuTemps.Enqueue(gpuTemp);
                }
                else
                {
                    GpuTemps.Enqueue(gpuTemp);
                }
            }
            public static int GetMinTemperature()
            {
                return Math.Min(Math.Min(GpuTemps.Min(), CpuTemps.Min()) - 5, InitialMinMeasurement);
            }
            public static int GetMaxTemperature()
            {
                return Math.Max(Math.Max(GpuTemps.Max(), CpuTemps.Max()) + 5, InitialMaxMeasurement);
            }
            public static Queue<int> CpuTemps { get; } = new Queue<int>();
            public static int CurrentCpuTemp
            {
                get
                {
                    return CpuTemps.Last();
                }
            }
            public static void AddCpuTemp(int cpuTemp)
            {
                if (CpuTemps.Count() > MaxTemperaturesCount)
                {
                    CpuTemps.Dequeue();
                    CpuTemps.Enqueue(cpuTemp);
                }
                else
                {
                    CpuTemps.Enqueue(cpuTemp);
                }
            }
        }

        public static class Window
        {
            public const double closeButtonScale = 0.05f;
            public const int MinWindowHeight = 400;
            public const int MaxWindowHeight = 1000;
            public const int MinWindowWidth = 400;
            public const int MaxWindowWidth = 1000;

            static Size windowSize = new Size(600, 400);

            public static int WindowHeight
            {
                get { return windowSize.Height; }
                set { windowSize.Height = value.Clamp(MinWindowHeight, MaxWindowHeight); }
            }
            public static int WindowWidth
            {
                get { return windowSize.Width; }
                set { windowSize.Width = value.Clamp(MinWindowWidth, MaxWindowWidth); }
            }
        }

        private bool isDragging = false;
        private Point mouseOffset;

        public static class HardwareInfo
        {
            static string cpuName;
            public static string CpuName
            {
                get
                {
                    Queue<string> queue = new Queue<string>(cpuName.Split(' ').Reverse());
                    return queue.Dequeue();
                }

                set
                {
                    cpuName = value;
                }
            }
            static string gpuName;
            public static string GpuName
            {
                get
                {
                    Queue<string> queue = new Queue<string>(gpuName.Split(' ').Reverse());
                    return String.Join(" ", (queue.Dequeue() + " " + queue.Dequeue()).Split(' ').Reverse());
                }

                set
                {
                    gpuName = value;
                }
            }

            public static int CurrentFPS;
        }

        public class UpdateVisitor : IVisitor
        {
            public void VisitComputer(IComputer computer)
            {
                computer.Traverse(this);
            }
            public void VisitHardware(IHardware hardware)
            {
                hardware.Update();
                foreach (IHardware subHardware in hardware.SubHardware) subHardware.Accept(this);
            }
            public void VisitSensor(ISensor sensor) { }
            public void VisitParameter(IParameter parameter) { }
        }

        void GetSystemInfo()
        {
            UpdateVisitor updateVisitor = new UpdateVisitor();
            Computer computer = new Computer();
            computer.Open();
            computer.CPUEnabled = true;
            computer.GPUEnabled = true;
            computer.Accept(updateVisitor);
            for (int i = 0; i < computer.Hardware.Length; i++)
            {
                if (computer.Hardware[i].HardwareType == HardwareType.CPU || computer.Hardware[i].HardwareType == HardwareType.GpuNvidia)
                {
                    for (int j = 0; j < computer.Hardware[i].Sensors.Length; j++)
                    {
                        if (computer.Hardware[i].Sensors[j].SensorType == SensorType.Temperature)
                        {
                            if (computer.Hardware[i].HardwareType == HardwareType.CPU && computer.Hardware[i].Sensors[j].Name == "CPU Package")
                            {
                                Temperatures.AddCpuTemp((int)computer.Hardware[i].Sensors[j].Value);
                                HardwareInfo.CpuName = computer.Hardware[i].Name;
                            }

                            if (computer.Hardware[i].HardwareType == HardwareType.GpuNvidia && computer.Hardware[i].Sensors[j].Name == "GPU Core")
                            {
                                Temperatures.AddGpuTemp((int)computer.Hardware[i].Sensors[j].Value);
                                HardwareInfo.GpuName = computer.Hardware[i].Name;
                            }
                        }

                    }
                }
            }
            computer.Close();
            //Console.WriteLine(String.Join(", ", Temperatures.GpuTemps) + "; " + String.Join(", ", Temperatures.CpuTemps));
        }

        public MainForm()
        {
            InitializeComponent();

            GetSystemInfo();

            this.ClientSize = new Size(Window.WindowWidth, Window.WindowHeight);
            this.FormBorderStyle = FormBorderStyle.None;

            button1.Size = new Size((int)(Window.WindowWidth * Window.closeButtonScale), (int)(Window.WindowHeight * Window.closeButtonScale));
            button1.Location = new Point(Window.WindowWidth - button1.Width, 0);

            pictureBox1.Location = new Point(0, 0);
            pictureBox1.Width = Window.WindowWidth;
            pictureBox1.Height = Window.WindowHeight;

            TemperatureGraph.ColumnWidth = TemperatureGraph.Bmp.Width / TemperatureGraph.Columns;

            Temperatures.CurrentMinMeasurement = Temperatures.GetMinTemperature();
            Temperatures.CurrentMaxMeasurement = Temperatures.GetMaxTemperature();

            Temperatures.MinMaxTemperatureDelta = (int)Math.Round((float)(Temperatures.CurrentMaxMeasurement - Temperatures.CurrentMinMeasurement) / 10) * 10;

            TemperatureGraph.Rows = Temperatures.MinMaxTemperatureDelta;// Math.Min(Math.Max(tempDiff, minVerticalStepsCount), maxVerticalStepsCount); //Math.Max(Math.Max(tempDiff, minVerticalStepsCount), maxVerticalStepsCount);
            TemperatureGraph.RowHeight = Math.Min(Math.Max(Temperatures.MinMaxTemperatureDelta, TemperatureGraph.MinRows), TemperatureGraph.MaxRows);
            while (TemperatureGraph.Bmp.Height % TemperatureGraph.Rows != 0)
            {
                TemperatureGraph.Bmp = new Bitmap(TemperatureGraph.Bmp.Width, TemperatureGraph.Bmp.Height + 1);
                //Console.WriteLine(TemperatureGraph.Bmp.Height);
            }

            TemperatureGraph.RowHeight = TemperatureGraph.Bmp.Height / TemperatureGraph.Rows;

            RenderImage();
        }

        void RenderImage()
        {
            button1.Size = new Size((int)(Window.WindowWidth * Window.closeButtonScale), (int)(Window.WindowHeight * Window.closeButtonScale));
            button1.Location = new Point(Window.WindowWidth - button1.Width, 0);

            //bmpWidth = (int)(windowWidth * bmpScale);
            //bmpHeight = (int)(windowHeight * bmpScale);

            TemperatureGraph.ColumnWidth = TemperatureGraph.Bmp.Width / TemperatureGraph.Columns;

            // = Math.Max(cpuTemps.AsQueryable().Max(), gpuTemps.AsQueryable().Max()) - Math.Min(cpuTemps.AsQueryable().Min(), gpuTemps.AsQueryable().Min());

            Temperatures.CurrentMinMeasurement = Temperatures.GetMinTemperature();
            Temperatures.CurrentMaxMeasurement = Temperatures.GetMaxTemperature();

            while (Temperatures.CurrentMaxMeasurement % 5 != 0)
                Temperatures.CurrentMaxMeasurement++;
            while (Temperatures.CurrentMinMeasurement % 5 != 0)
                Temperatures.CurrentMinMeasurement--;

            Temperatures.MinMaxTemperatureDelta = Temperatures.GetMaxTemperature() - Temperatures.GetMinTemperature();//(int)Math.Round((float)(currentMaxTemp - currentMinTemp) / 10) * 10;

            TemperatureGraph.Rows = Temperatures.MinMaxTemperatureDelta;// Math.Min(Math.Max(tempDiff, minVerticalStepsCount), maxVerticalStepsCount); //Math.Max(Math.Max(tempDiff, minVerticalStepsCount), maxVerticalStepsCount);
            while (TemperatureGraph.Bmp.Height % TemperatureGraph.Rows != 0)
            {
                TemperatureGraph.Bmp = new Bitmap(TemperatureGraph.Bmp.Width, TemperatureGraph.Bmp.Height + 1);
            }

            TemperatureGraph.RowHeight = TemperatureGraph.Bmp.Height / TemperatureGraph.Rows;

            TemperatureGraph.Bmp = new Bitmap(TemperatureGraph.Bmp.Width, TemperatureGraph.Bmp.Height);

            TemperatureGraph.BlackenBitmap();

            RenderGrid();

            RenderBottomLine();

            pictureBox1.Image = Ext.ResizeImage(TemperatureGraph.Bmp, Window.WindowWidth, Window.WindowHeight);
        }

        void RenderBottomLine()
        {
            // STUFF TO DISPLAY AT THE BOTTOM OF BMP
        }

        void RenderGrid()
        {
            //Console.WriteLine("Current verticalStepsCount = " + verticalStepsCount);
            //Console.WriteLine(gridStepY.ToString() + " " + bmpHeight.ToString());
            //Console.WriteLine("min = " + currentMinTemp.ToString() + "; max = " + currentMaxTemp.ToString());

            for (int i = 0; i < TemperatureGraph.Bmp.Width; i++)
            {
                for (int j = 0; j < TemperatureGraph.Bmp.Height; j++)
                {
                    if (i % TemperatureGraph.ColumnWidth == 0 && j % TemperatureGraph.RowHeight == 0)
                    {
                        using (Graphics gfx = Graphics.FromImage(TemperatureGraph.Bmp))
                        {
                            using (SolidBrush brush = new SolidBrush(Color.FromArgb(0, 125, 0)))
                            {
                                gfx.FillRectangle(brush, i, j, 1, 1);
                            }
                        }
                    }
                }
            }

            //Console.WriteLine("vertical steps = " + (float)(bmpHeight / gridStepY));
            //Console.WriteLine("bmpHeight % gridStepY = " + (float)(0 % gridStepY));

            for (int j = 0; j <= TemperatureGraph.Bmp.Height; j++)
            {
                ////Console.WriteLine(j);
                using (Graphics gfx = Graphics.FromImage(TemperatureGraph.Bmp))
                {
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(0, 255, 0)))
                    {
                        int i = (int)TemperatureGraph.ColumnWidth * 0;
                        if (j % TemperatureGraph.RowHeight == 0 && ((TemperatureGraph.Bmp.Height - j) / TemperatureGraph.RowHeight) % 10 == 0)
                        {
                            StringFormat format = new StringFormat()
                            {
                                Alignment = StringAlignment.Near,
                                LineAlignment = StringAlignment.Center
                            };

                            for (int ii = 0; ii <= TemperatureGraph.Bmp.Width; ii += TemperatureGraph.ColumnWidth)
                            {
                                using (SolidBrush brush2 = new SolidBrush(Color.FromArgb(0, 255, 0)))
                                {
                                    gfx.FillRectangle(brush2, ii, j, 1, 1);
                                }
                            }

                            RectangleF rectf = new RectangleF(i, j - TemperatureGraph.TextHeight / 2, TemperatureGraph.TextWidth, TemperatureGraph.TextHeight);

                            gfx.DrawString("" + ((TemperatureGraph.Bmp.Height - j) / TemperatureGraph.RowHeight + Temperatures.CurrentMinMeasurement), new Font("Tahoma", TemperatureGraph.FontSize), Brushes.Green, rectf, format);
                        }
                    }
                }
            }

            Pen RedPen = new Pen(Color.Red, 3);
            for (int i = 0; i < Temperatures.GpuTemps.Count - 1; i++)
            {
                using (Graphics gfx = Graphics.FromImage(TemperatureGraph.Bmp))
                {
                    try
                    {
                        PointF point1 = new PointF(i * TemperatureGraph.ColumnWidth, TemperatureGraph.Bmp.Height - ((Temperatures.GpuTemps.ToArray()[i] - Temperatures.GetMinTemperature()) * TemperatureGraph.RowHeight));
                        PointF point2 = new PointF((i + 1) * TemperatureGraph.ColumnWidth, TemperatureGraph.Bmp.Height - ((Temperatures.GpuTemps.ToArray()[i + 1] - Temperatures.GetMinTemperature()) * TemperatureGraph.RowHeight));
                        gfx.DrawLine(RedPen, point1, point2);
                    }
                    catch (Exception e)
                    {
                        //Console.WriteLine("Ошибка отрисовки линии шага температуры - " + e.Message);
                    }
                }
            }

            Pen yellowPen = new Pen(Color.Yellow, 3);
            for (int i = 0; i < Temperatures.CpuTemps.Count - 1; i++)
            {
                using (Graphics gfx = Graphics.FromImage(TemperatureGraph.Bmp))
                {
                    try
                    {
                        PointF point1 = new PointF(i * TemperatureGraph.ColumnWidth, TemperatureGraph.Bmp.Height - ((Temperatures.CpuTemps.ToArray()[i] - Temperatures.GetMinTemperature()) * TemperatureGraph.RowHeight));
                        PointF point2 = new PointF((i + 1) * TemperatureGraph.ColumnWidth, TemperatureGraph.Bmp.Height - ((Temperatures.CpuTemps.ToArray()[i + 1] - Temperatures.GetMinTemperature()) * TemperatureGraph.RowHeight));
                        gfx.DrawLine(yellowPen, point1, point2);
                    }
                    catch (Exception e)
                    {
                        //Console.WriteLine("Ошибка отрисовки линии шага температуры - " + e.Message);
                    }
                }
            }
            try
            {
                using (Graphics gfx = Graphics.FromImage(TemperatureGraph.Bmp))
                {
                    StringFormat format = new StringFormat()
                    {
                        Alignment = StringAlignment.Near,
                        LineAlignment = StringAlignment.Center
                    };
                    RectangleF rectf = new RectangleF((Temperatures.GpuTemps.Count - 1) * TemperatureGraph.ColumnWidth, TemperatureGraph.Bmp.Height - ((Temperatures.GpuTemps.ToArray()[Temperatures.CpuTemps.Count - 1] - Temperatures.GetMinTemperature()) * TemperatureGraph.RowHeight), TemperatureGraph.TextWidth, TemperatureGraph.TextHeight + 2);
                    gfx.DrawString(HardwareInfo.GpuName + " " + Temperatures.CurrentGpuTemp + "°", new Font("Tahoma", TemperatureGraph.FontSize), Brushes.Red, rectf, format);
                    //Console.WriteLine("Drawin on = " + rectf.X + " " + rectf.Y + " w,h = " + rectf.Width + " " + rectf.Height);
                }

                using (Graphics gfx = Graphics.FromImage(TemperatureGraph.Bmp))
                {
                    StringFormat format = new StringFormat()
                    {
                        Alignment = StringAlignment.Near,
                        LineAlignment = StringAlignment.Center
                    };
                    RectangleF rectf = new RectangleF((Temperatures.CpuTemps.Count - 1) * TemperatureGraph.ColumnWidth, TemperatureGraph.Bmp.Height - ((Temperatures.CpuTemps.ToArray()[Temperatures.CpuTemps.Count - 1] - Temperatures.GetMinTemperature()) * TemperatureGraph.RowHeight), TemperatureGraph.TextWidth, TemperatureGraph.TextHeight + 2);
                    gfx.DrawString(HardwareInfo.CpuName + " " + Temperatures.CurrentCpuTemp + "°", new Font("Tahoma", TemperatureGraph.FontSize), Brushes.Yellow, rectf, format);
                }
            }
            catch { }

        }

        void MainFormSizeChanged(object sender, EventArgs e)
        {
            //RenderImage();
        }


        void MainFormClick(object sender, EventArgs e)
        {

        }

        void PictureBox1Click(object sender, EventArgs e)
        {

            //MirrorImage();
            ////Console.WriteLine("Flip called" + "\n" + "width : " + pictureBox1.Width);
            ////Console.WriteLine(this.Width + " " + this.Height);
        }

        void MirrorImage()
        {
            //			using (Graphics g = Graphics.FromImage(bmp))
            //			{
            //				g.ScaleTransform(1, -1);
            //			}

            //Image img = bmp;

            //img.RotateFlip(RotateFlipType.Rotate180FlipX);

            //pictureBox1.Image = img;

            //pictureBox1.Refresh();

            //this.Width = pictureBox1.Width;
            //this.Height = pictureBox1.Height;
        }

        void MainFormMouseDown(object sender, MouseEventArgs e)
        {

            ////Console.WriteLine("Down");
            // Если зажата левая кнопка мыши
            if (e.Button == MouseButtons.Left)
            {
                ////Console.WriteLine("MouseLeft");
                // Запоминаем позицию мыши относительно окна
                mouseOffset = new Point(-e.X, -e.Y);
                isDragging = true;
            }
        }

        void MainFormMouseUp(object sender, MouseEventArgs e)
        {

            ////Console.WriteLine("Up");
            // Если отпущена левая кнопка мыши
            if (e.Button == MouseButtons.Left)
            {
                isDragging = false;
            }
        }

        void MainFormMouseMove(object sender, MouseEventArgs e)
        {

            ////Console.WriteLine("Move" + (isDragging ? 1 : 0));
            // Если окно перетаскивается
            if (isDragging == true)
            {
                // Получаем текущую позицию мыши на экране
                Point mousePos = Control.MousePosition;
                ////Console.WriteLine("MousePosition = " + Control.MousePosition.X + " " + Control.MousePosition.Y);

                // Вычисляем новое положение окна с учетом позиции мыши
                mousePos.Offset(mouseOffset.X, mouseOffset.Y);
                this.Location = mousePos;
            }
        }

        void GetSystemInfoAsync()
        {
            //Task t1 = Task.Run(() => GetSystemInfo());
            //new Task(() => GetSystemInfo()).Start();
        }


        private void timer1_Tick(object sender, EventArgs e)
        {
            GetSystemInfo();
            RenderImage();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }
    }
}
