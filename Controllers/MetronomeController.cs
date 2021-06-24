using System;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Threading;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;


namespace loopman
{
    class MetronomeController
    {
        public double SixteenthMS = 0;

        public int CountBars = 1;
        public int CountBeats = 1;
        public int Count16ths = 1;

        public int TimeSigNum = 4;
        public int TimeSigDen = 4;

        public bool isReady = false;
        public bool isPlaying = false;

        private AsioInputPatcher inputPatcher;

        private Stopwatch timecheck = new Stopwatch();

        private TextBlock tbBeatClockBars = null;
        private TextBlock tbBeatClockBeats = null;
        private TextBlock tbBeatClockSixteenths = null;

        private const double clickVolumeLogExp = 2.718;
        private float clickVolumeScale = 1;

        public bool Mute;
        public bool CountingIn;
        public int CountInBars;


        public MetronomeController(TextBlock _beatClockBars, TextBlock _beatClockBeats, TextBlock _beatClockSixteenths)
        {
            tbBeatClockBars = _beatClockBars;
            tbBeatClockBeats = _beatClockBeats;
            tbBeatClockSixteenths = _beatClockSixteenths;

            CountBars = CountBeats = Count16ths = 1;
            UpdateBeatClock(false);

            // initialize volume
            clickVolumeScale = (float)(1 / Math.Pow(100, clickVolumeLogExp));
            timecheck.Start();
        }

        public void SetPatcher(AsioInputPatcher _patcher)
        {
            inputPatcher = _patcher;
            isReady = true;
        }

        // Test the metronome timer and click if it's time
        public bool Poll()
        {
            if (!isPlaying
             || !isReady
             || timecheck.ElapsedMilliseconds < SixteenthMS)
                return false;

            // we're running, ready, and a new 16th has elapsed
            timecheck.Restart();
            int newBeatBar = UpdateBeatClock(true);

            if (!Mute && CountingIn && ((newBeatBar & 1) == 1))
            {
                inputPatcher.Click((newBeatBar & 2) == 2);
            }
            return (newBeatBar & 2) == 2;
        }

        public void Reset()
        {
            CountBars = CountBeats = Count16ths = 1;
            UpdateBeatClock(false);

            // set to a rollover for the first click to sound on the start of the metronome
            CountBars = 0;
            CountBeats = TimeSigNum;
            Count16ths = 16 / TimeSigDen;

            CountingIn = true;
        }

        public void Play(int _countInBars = -1)
        {
            isPlaying = true;
            Reset();
            CountInBars = _countInBars;
        }

        public void Stop()
        {
            isPlaying = false;
        }

        // this is called every 16th from the poller
        public int UpdateBeatClock(bool add16th)
        {
            int newBeatBar = 0;

            // add a sixteenth to the count if requested
            if (add16th)
            {
                if (Count16ths < (16 / TimeSigDen)) Count16ths++;
                else
                {
                    Count16ths = 1;
                    newBeatBar |= 1;
                    if (CountBeats < TimeSigNum) CountBeats++;
                    else
                    {
                        CountBeats = 1;
                        CountBars++;
                        if ((CountInBars != -1) && (CountBars > CountInBars)) CountingIn = false;
                        if (CountBars > 99) CountBars = 1;
                        newBeatBar |= 2;
                    }
                }
            }

            // update the clock on the UI
            //tbBeatClockBars.Dispatcher.Invoke(() => { tbBeatClockBars.Text = CountBars.ToString(); });
            //tbBeatClockBeats.Dispatcher.Invoke(() => { tbBeatClockBeats.Text = CountBeats.ToString(); });
            //tbBeatClockSixteenths.Dispatcher.Invoke(() => { tbBeatClockSixteenths.Text = Count16ths.ToString(); });

            return newBeatBar;
        }

        // Calculate tempo in milliseconds from bpm
        public void SetTempo(double tempo)
        {
            SixteenthMS = 1000 * 60 / tempo / 4;
        }

        // Set the metronome volume from a linear control in the range (0-100), output is 0-1.0
        public void SetLogVolume(double ctl)
        {
            inputPatcher.clickVolume = (float)(clickVolumeScale * Math.Pow(ctl, clickVolumeLogExp));
        }

        public void SetPan(float pan)
        {
            inputPatcher.clickPan = pan;
        }

    }
}
