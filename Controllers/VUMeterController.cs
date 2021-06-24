using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Shapes;


namespace loopman
{
    class VUMeterController
    {
        private Brush rDefaultBrush;

        private ProgressBar pbInLeft = null;
        private Rectangle rInLeft = null;
        private ProgressBar pbInRight = null;
        private Rectangle rInRight = null;

        private ProgressBar pbOutLeft = null;
        private Rectangle rOutLeft = null;
        private ProgressBar pbOutRight = null;
        private Rectangle rOutRight = null;

        private int iRedPersistLeft = 0;
        private int iRedPersistRight = 0;
        private const int redPersistReset = 50;

        private int ResetCount;
        private const int ResetCountMax = 5;

        private AsioInputPatcher inputPatcher = null;


        public VUMeterController(
            ProgressBar pbIn1, Rectangle rIn1, ProgressBar pbIn2, Rectangle rIn2,
            ProgressBar pbOut1, Rectangle rOut1, ProgressBar pbOut2, Rectangle rOut2
        )
        {
            pbInLeft = pbIn1;   rInLeft = rIn1;
            pbInRight = pbIn2;  rInRight = rIn2;

            pbOutLeft = pbOut1;     rOutLeft = rOut1;
            pbOutRight = pbOut2;    rOutRight = rOut2;

            rDefaultBrush = rInLeft.Fill;
            DispatcherTimer timInput = new DispatcherTimer();
            timInput.Tick += timInput_Tick;
            timInput.Interval = new TimeSpan(100000); // 100nS units
            timInput.Start();

            ResetCount = ResetCountMax;
        }

        public void SetPatcher(AsioInputPatcher patcher)
        {
            inputPatcher = patcher;
        }

        private void timInput_Tick(object sender, EventArgs e)
        {
            if (inputPatcher == null) return;
            if (inputPatcher.channelPeakIn == null) return;

            float peak = inputPatcher.channelPeakIn[0];
            pbInLeft.Value = peak;
            if (peak >= 0.95f)
            {
                rInLeft.Fill = Brushes.Red;
                iRedPersistLeft = redPersistReset;
            } else if (iRedPersistLeft > 0)
            {
                if (--iRedPersistLeft == 0)
                    rInLeft.Fill = rDefaultBrush;
            }

            peak = inputPatcher.channelPeakIn[1];
            pbInRight.Value = peak;
            if (peak >= 0.95f)
            {
                rInRight.Fill = Brushes.Red;
                iRedPersistRight = redPersistReset;
            }
            else if (iRedPersistRight > 0)
            {
                if (--iRedPersistRight == 0)
                    rInRight.Fill = rDefaultBrush;
            }


            peak = inputPatcher.channelPeakOut[0];
            pbOutLeft.Value = peak;
            if (peak >= 0.95f)
            {
                rOutLeft.Fill = Brushes.Red;
                iRedPersistLeft = redPersistReset;
            }
            else if (iRedPersistLeft > 0)
            {
                if (--iRedPersistLeft == 0)
                    rOutLeft.Fill = rDefaultBrush;
            }

            peak = inputPatcher.channelPeakOut[1];
            pbOutRight.Value = peak;
            if (peak >= 0.95f)
            {
                rOutRight.Fill = Brushes.Red;
                iRedPersistRight = redPersistReset;
            }
            else if (iRedPersistRight > 0)
            {
                if (--iRedPersistRight == 0)
                    rOutRight.Fill = rDefaultBrush;
            }


            if (--ResetCount <= 0)
            {
                inputPatcher.ResetPeakIn();
                inputPatcher.ResetPeakOut();
                ResetCount = ResetCountMax;
            }
        }

    }
}
