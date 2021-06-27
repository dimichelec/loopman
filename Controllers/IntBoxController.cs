using System;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using System.Windows.Media;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace loopman
{
    class IntBoxController
    {
        public int ibValue;
        private int rbMin = 1;
        private int rbMax = 16;
        private Brush rbDefaultBG;
        private Brush activeBrush = SystemColors.GradientActiveCaptionBrush;
        private Brush inactiveBrush = SystemColors.GradientInactiveCaptionBrush;
        private bool ibEditMode;
        private int ibNumberMode;

        public void StartCapture(TextBlock tb)
        {
            ibValue = int.Parse(tb.Text);
            Mouse.Capture(tb, CaptureMode.Element);
            rbDefaultBG = tb.Background;
            tb.Background = activeBrush;
            tb.Focus();
            ibEditMode = false;
            var args = tb.Tag.ToString().Split(',');
            rbMin = int.Parse(args[0]);
            rbMax = int.Parse(args[1]);
            ibNumberMode = 0;
            if (args.Length > 2)
            {
                ibNumberMode = int.Parse(args[2]);
            }
            if (ibNumberMode == 1) ibValue = (int)Math.Log2(ibValue);
        }

        public void EndCapture(TextBlock tb)
        {
            Mouse.Capture(tb, CaptureMode.None);
            if (ibEditMode)
            {
                if (tb.Text == "") tb.Text = rbMin.ToString();
                int i = int.Parse(tb.Text);
                if (ibNumberMode == 1) i = (int)Math.Round(Math.Log2(i));
                if (i < rbMin) i = rbMin;
                if (i > rbMax) i = rbMax;
                if (ibNumberMode == 1) i = (int)Math.Pow(2, i);
                tb.Text = i.ToString();
            }
            tb.Background = rbDefaultBG;
        }

        public void PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            TextBlock tb = (TextBlock)sender;
            if (Mouse.Captured == tb)
            {
                if ((e.GetPosition(tb).X < 0) || (e.GetPosition(tb).X > tb.Width)
                 || (e.GetPosition(tb).Y < 0) || (e.GetPosition(tb).Y > tb.Height))
                {
                    EndCapture(tb);
                }
                else
                {
                    tb.Background = activeBrush;
                }
            }
            else
            {
                StartCapture(tb);
            }
        }

        public void PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            TextBlock tb = (TextBlock)sender;
            if (Mouse.Captured == tb)
            {
                int i = int.Parse(tb.Text);
                i = (ibNumberMode == 1) ? (int)Math.Log2(i) : i;
                if (ibValue != i)
                {
                    ibValue = i;
                    EndCapture(tb);
                } else
                {
                    tb.Background = inactiveBrush;
                }
            }
            e.Handled = true;
        }

        public void MouseMove(object sender, MouseEventArgs e, Canvas cMain)
        {
            TextBlock tb = (TextBlock)sender;
            if (Mouse.Captured == tb)
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    int i = (int)(e.GetPosition(tb).Y / 5);
                    i = ibValue - i;
                    if (i < rbMin) i = rbMin;
                    if (i > rbMax) i = rbMax;
                    if (ibNumberMode == 1) i = (int)Math.Pow(2, i);
                    tb.Text = i.ToString();
                }
                else
                {
                    if ((e.GetPosition(cMain).X < 0) || (e.GetPosition(cMain).X > cMain.DesiredSize.Width)
                     || (e.GetPosition(cMain).Y < 0) || (e.GetPosition(cMain).Y > cMain.DesiredSize.Height))
                    {
                        EndCapture(tb);
                    }
                }
            }
        }

        public void MouseWheel(object sender, MouseWheelEventArgs e)
        {
            TextBlock tb = (TextBlock)sender;
            if (Mouse.Captured != tb) return;
            int i = e.Delta / 120;
            i = ibValue + i;
            if (i < rbMin) i = rbMin;
            if (i > rbMax) i = rbMax;
            ibValue = i;
            if (ibNumberMode == 1) i = (int)Math.Pow(2, i);
            tb.Text = i.ToString();
        }

        public void PreviewKeyDown(object sender, KeyEventArgs e)
        {
            TextBlock tb = (TextBlock)sender;
            if (Mouse.Captured != tb) return;

            if (e.Key == Key.Up)
            {
                if (ibValue < rbMax)
                {
                    ibValue++;
                    int i = (ibNumberMode == 1) ? (int)Math.Pow(2, ibValue) : ibValue;
                    tb.Text = i.ToString();
                }
            }
            else if (e.Key == Key.Down)
            {
                if (ibValue > rbMin)
                {
                    ibValue--;
                    int i = (ibNumberMode == 1) ? (int)Math.Pow(2, ibValue) : ibValue;
                    tb.Text = i.ToString();
                }
            }
            else if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                EndCapture(tb);
            }
            else
            {
                var k = new KeyConverter();
                string sk = k.ConvertToString(null, e.Key);
                if (sk.StartsWith("NumPad"))
                {
                    sk = sk.Substring(6);
                }

                if (sk[0] >= '0' && sk[0] <= '9')
                {
                    if (!ibEditMode)
                    {
                        ibEditMode = true;
                        tb.Text = sk;
                    }
                    else
                    {
                        tb.Text += sk;
                    }
                }
                else if ((e.Key == Key.Back) && ibEditMode)
                {
                    if (tb.Text.Length > 0) tb.Text = tb.Text.Substring(0, tb.Text.Length - 1);
                }
            }

            e.Handled = true;
        }


    }
}
