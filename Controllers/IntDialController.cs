using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shapes;
using System.ComponentModel;
using System.Threading;


namespace loopman
{
    class IntDialController
    {
        private int idValue;
        private int idLatest;
        private int idNumberMode;
        private int iSpeed;
        private float fRadius;
        private readonly int idMin = 0;
        private readonly int idMax = 100;
        private Path idPath;
        private Brush idDefaultBG;
        private readonly Brush activeBrush = SystemColors.GradientActiveCaptionBrush;
        public bool doubleClicked;

        public void ChangeValue(Path pa, int value)
        {
            var args = pa.Tag.ToString().Split(',');
            args[0] = value.ToString();
            pa.Tag = string.Join(',', args);
        }

        public int GetValue(Path pa, int index = 0)
        {
            return int.Parse(pa.Tag.ToString().Split(',')[index]);
        }

        public void StartCapture(Ellipse el)
        {
            idValue = 100 - GetValue(idPath);
            idNumberMode = GetValue(idPath, 1);
            iSpeed = GetValue(idPath, 2);
            fRadius = (float)GetValue(idPath, 3);
            idLatest = idValue;
            Mouse.Capture(el, CaptureMode.Element);
            idDefaultBG = el.Fill;
            el.Fill = activeBrush;
            doubleClicked = false;
        }

        public void EndCapture(Ellipse el)
        {
            Mouse.Capture(el, CaptureMode.None);
            el.Fill = idDefaultBG;
        }

        public bool MouseMove(object sender, MouseEventArgs e)
        {
            Ellipse el = (Ellipse)sender;
            if (Mouse.Captured == el)
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    int i = (int)(e.GetPosition(el).Y / iSpeed);
                    i = idValue + i;
                    if (i < idMin) i = idMin;
                    if (i > idMax) i = idMax;
                    idLatest = i;
                    idPath.Data = GetArc(i, idNumberMode, fRadius);
                    ChangeValue(idPath, 100 - idLatest);
                    return true;
                }
            }
            return false;
        }

        public void PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            Ellipse el = (Ellipse)sender;
            if (Mouse.Captured == el)
            {
                if ((e.GetPosition(el).X < 0) || (e.GetPosition(el).X > el.Width)
                 || (e.GetPosition(el).Y < 0) || (e.GetPosition(el).Y > el.Height))
                {
                    EndCapture(el);
                }
                else
                {
                    el.Fill = activeBrush;
                }
            }
            else
            {
                idPath = (Path)el.FindName(el.Tag.ToString());
                if (idPath.Visibility != Visibility.Hidden) StartCapture(el);
            }
        }

        public void PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            Ellipse el = (Ellipse)sender;
            if (Mouse.Captured == el)
            {
                if (!doubleClicked)
                {
                    idValue = idLatest;
                    ChangeValue(idPath, 100 - idLatest);
                }
                EndCapture(el);
            }
            e.Handled = true;
        }


        public Geometry GetArc(int percent, int mode = 0, float r = 16f)
        {
            //float r = 16f;
            float cX = r + 2f; // 18f;
            float cY = r + 2f; // 18f;

            float a = (float)(45f + (float)((mode == 0) ? percent : percent - 1f) * 2.7f) / 180f * (float)Math.PI;
            float b = (float)(45f + (float)((mode == 0) ?    100f : percent + 1f) * 2.7f) / 180f * (float)Math.PI;

            if (a > b) a = b;

            var converter = TypeDescriptor.GetConverter(typeof(Geometry));
            return (Geometry)converter.ConvertFrom(string.Format(
                "M {0},{1} A {2},{2}, 0, {3}, 0, {4},{5}",
                cX + r * (float)Math.Sin(a), cY + r * (float)Math.Cos(a),
                r, ((b - a) > Math.PI) ? 1 : 0,
                cX + r * (float)Math.Sin(b), cY + r * (float)Math.Cos(b)
            ));
        }


    }
}
