﻿using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Diagnostics;
using System.Management;
using System.Windows.Threading;
using System.Threading;
using System.Windows.Shapes;
using System.Configuration;
using System.ComponentModel;

using NAudio.Wave;
using NAudio.Wave.Asio;
using NAudio.Wave.SampleProviders;
using NAudio.Midi;
using NAudio.CoreAudioApi;

namespace loopman
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 
    public partial class MainWindow : Window
    {
        private AsioOut asioOut;
        private AsioInputPatcher inputPatcher;
        private int sampleRate = -1;
        private int inputLatencies;
        private int outputLatencies;

        private MidiIn midiIn;
        private DispatcherTimer midiTimer;
        private Brush bMidiDefault;
        private Brush bMidiPlayRecordDefault;
        private Brush bMidiStopDefault;
        private int iMidiLearn;
        private int iMidiPlayRecord;
        private int iMidiStop;

        private int iTempo;
        private bool bCountingIn;
        private bool bMetroPlaying;
        private bool bMetroMute;
        private bool bMetroClick;
        private int halfBeatMS;
        private int iCountBeats;
        private int iCountInBeats;
        private bool halfBeat;
        private int iRecordBeats;


        private readonly Stopwatch timecheck = new();


        private const double clickVolumeLogExp = 2.718;
        private float clickVolumeScale = (float)(1 / Math.Pow(100, clickVolumeLogExp));

        private readonly IntBoxController ibController = new();
        private readonly IntDialController idController = new();

        private readonly VUMeterController vuMeter;


        // ----------------------------------------------------------------------------
        // Main Window

        public MainWindow()
        {
            InitializeComponent();

            // recall window location
            Left = Settings.Default.WindowLeft;
            Top = Settings.Default.WindowTop;

            // init main window control controllers 
            vuMeter = new VUMeterController(
                pbInput1, rInput1, pbInput2, rInput2,
                pbOutput1, rOutput1, pbOutput2, rOutput2
            );

            midiTimer = new DispatcherTimer();
            midiTimer.Tick += new EventHandler(midiTimer_Tick);
            midiTimer.Interval = new TimeSpan(0, 0, 0, 0, 250);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // init controls from saved settings
            ChangeAudioDriver(Settings.Default.AudioDriverName);
            ChangeMIDIDevice(Settings.Default.MIDIDriverName);

            // init noise gate controls
            idController.ChangeValue(idNoiseGate, Settings.Default.NoiseGateThreshold);
            idNoiseGate.Data = idController.GetArc(100 - Settings.Default.NoiseGateThreshold, 1, 8);
            inputPatcher.SetNoiseGateThreshold(Settings.Default.NoiseGateThreshold);
            idNoiseGate.Visibility = (Settings.Default.NoiseGateEnabled) ? Visibility.Visible : Visibility.Hidden;
            inputPatcher.EnableNoiseGate(Settings.Default.NoiseGateEnabled);

            // init midi controller
            iMidiPlayRecord = Settings.Default.MIDIPlayRecord;
            iMidiStop = Settings.Default.MIDIStop;
            bMidiDefault = tbMidi.Background;
            bMidiPlayRecordDefault = bMidiPlayRecord.Background;
            bMidiStopDefault = bMidiStop.Background;

            // init metronome controller
            ibTempo.Text = Settings.Default.Tempo.ToString();

            idController.ChangeValue(idMetroVolume, Settings.Default.MetroVolume);
            idMetroVolume.Data = idController.GetArc(100 - Settings.Default.MetroVolume);
            inputPatcher.clickVolume = (float)(clickVolumeScale * Math.Pow(Settings.Default.MetroVolume, clickVolumeLogExp));

            idMetroVolume.Visibility = (Settings.Default.MetroMute) ? Visibility.Hidden : Visibility.Visible;
            bMetroMute = Settings.Default.MetroMute;

            int i = (int)((Settings.Default.MetroPan + 1f) * 50f);
            idController.ChangeValue(idMetroPan, i);
            idMetroPan.Data = idController.GetArc(100 - i, 1);
            inputPatcher.clickPan = Settings.Default.MetroPan;

            iTempo = int.Parse(ibTempo.Text);
            halfBeatMS = (int)(1000f * 60f / (float)iTempo / 2f);

            // init recorder parameters
            ibCountInBeats.Text = Settings.Default.CountInBeats.ToString();
            ibRecordBeats.Text = Settings.Default.RecordBeats.ToString();
            rPlayRecord.Stroke = Brushes.Transparent;

            DispatcherTimer timServices = new DispatcherTimer();
            timServices.Tick += timServices_Tick;
            timServices.Interval = new TimeSpan(1000); // 100nS units
            timServices.Start();

        }

        private void timServices_Tick(object sender, EventArgs e)
        {
            if (inputPatcher.iRecordMax > 0)
                pbMemory.Value = (int)(100f * (float)inputPatcher.iRecord / inputPatcher.iRecordMax);

        }


        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Settings.Default.WindowLeft = this.Left;
            Settings.Default.WindowTop = this.Top;

            // save noise gate parameters
            Settings.Default.NoiseGateThreshold = inputPatcher.GetNoiseGateThreshold();
            Settings.Default.NoiseGateEnabled = (idNoiseGate.Visibility == Visibility.Visible);

            // save metronome parameters
            Settings.Default.Tempo = int.Parse(ibTempo.Text);
            Settings.Default.CountInBeats = int.Parse(ibCountInBeats.Text);
            Settings.Default.RecordBeats = int.Parse(ibRecordBeats.Text);

            Settings.Default.MetroVolume = idController.GetValue(idMetroVolume);
            Settings.Default.MetroMute = (idMetroVolume.Visibility == Visibility.Hidden);
            Settings.Default.MetroPan = (float)((float)(idController.GetValue(idMetroPan) - 50) / 50f);

            // save midi controller parameters
            Settings.Default.MIDIPlayRecord = iMidiPlayRecord;
            Settings.Default.MIDIStop = iMidiStop;

            Settings.Default.Save();

            ResetAsio();
            Application.Current.Shutdown();
        }

        private void FocusOnNext(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TraversalRequest request = new TraversalRequest(FocusNavigationDirection.Next);
                MoveFocus(request);
            }
        }

        private void SelectAll(object sender, RoutedEventArgs e)
        {
            TextBox tb = (TextBox)sender;
            tb.Dispatcher.BeginInvoke(new Action(() => tb.SelectAll()));
        }


        // ----------------------------------------------------------------------------

        public static void GetDevices()
        {
            var objSearcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_SoundDevice"
            );

            var objCollection = objSearcher.Get();
            foreach (var d in objCollection)
            {
                Debug.WriteLine("\nDevices\n--------------------------");
                foreach (var p in d.Properties)
                {
                    Debug.WriteLine($"{p.Name}:{p.Value}");
                }
            }
        }


        // ----------------------------------------------------------------------------
        // Recorder

        private enum RecordingStates { idle, armed, countIn, recording, playback };
        private RecordingStates recordingState = RecordingStates.idle;

        private void ResetRecorderState()
        {
            rPlayRecord.Stroke = Brushes.Transparent;
            recordingState = RecordingStates.idle;
        }

        private void StopRecorder()
        {
            ResetRecorderState();
            StopMetronome(false);
            inputPatcher.Stop();
        }

        private void PressPlayRecord()
        {
            switch (recordingState)
            {
                case RecordingStates.idle:
                    inputPatcher.InitRecording(300);
                    rPlayRecord.Stroke = Brushes.Yellow;
                    recordingState = RecordingStates.armed;
                    inputPatcher.Record();
                    iCountInBeats = int.Parse(ibCountInBeats.Text);
                    iRecordBeats = int.Parse(ibRecordBeats.Text);
                    StartMetronome(true);
                    break;

                case RecordingStates.recording:
                    inputPatcher.MarkLoopEnd();
                    inputPatcher.Play();
                    inputPatcher.isRecording = false;
                    rPlayRecord.Stroke = Brushes.Green;
                    recordingState = RecordingStates.playback;
                    StopMetronome(true);
                    break;

                default:
                    StopRecorder();
                    break;
            }
        }

        private void bPlayRecord_Click(object sender, RoutedEventArgs e)
        {
            PressPlayRecord();
        }

        private void bStop_Click(object sender, RoutedEventArgs e) => StopRecorder();


        // ----------------------------------------------------------------------------
        // Audio Driver Interface 

        void ChangeAudioDriver(string driverName)
        {
            // reset things
            ResetAsio();
            if (vuMeter != null) vuMeter.SetPatcher(null);
            sampleRate = inputLatencies = outputLatencies = 0;

            if (driverName == null) return;
            if (driverName == "") return;

            // first try getting the new device's driver capabilities
            AsioDriver drv;
            try
            {
                drv = AsioDriver.GetAsioDriverByName(driverName);
            }
            catch (Exception)
            {
                return;
            }
            if (!drv.Init(IntPtr.Zero)) return;

            sampleRate = (int)drv.GetSampleRate();
            drv.GetLatencies(out inputLatencies, out outputLatencies);

            // open output device
            asioOut = new AsioOut(driverName);
            asioOut.InputChannelOffset = 0;
            asioOut.ChannelOffset = 0;

            inputPatcher = new AsioInputPatcher((int)sampleRate, 2, 2);

            asioOut.AudioAvailable += OnAsioOutAudioAvailable;
            asioOut.InitRecordAndPlayback(new SampleToWaveProvider(inputPatcher), 2, 0);
            asioOut.Play();

            vuMeter.SetPatcher(inputPatcher);

            inputPatcher.clickVolume = (float)(clickVolumeScale * Math.Pow(Settings.Default.MetroVolume, clickVolumeLogExp));

            timecheck.Start();
        }

        void OnAsioOutAudioAvailable(object sender, AsioAudioAvailableEventArgs e)
        {
            inputPatcher.ProcessBuffer(
                e.InputBuffers, e.OutputBuffers,
                e.SamplesPerBuffer, e.AsioSampleType
            );

            if (bMetroPlaying && (timecheck.ElapsedMilliseconds >= halfBeatMS))
            {
                // we're running, ready, and a new haf-beat has elapsed
                timecheck.Restart();
                if (!halfBeat)
                {
                    Dispatcher.Invoke(() => { eBeat.Fill = Brushes.Transparent; });
                    halfBeat = true;
                }
                else
                {
                    Dispatcher.Invoke(() => { eBeat.Fill = Brushes.Red; });
                    halfBeat = false;
                    if(iCountInBeats > 0)
                    {
                        if (++iCountBeats > iCountInBeats)
                        {
                            bCountingIn = false;

                            if (recordingState == RecordingStates.armed)
                            {
                                inputPatcher.MarkLoopStart();
                                Dispatcher.Invoke(() => rPlayRecord.Stroke = Brushes.Red);
                                recordingState = RecordingStates.recording;
                            } 
                            else if (recordingState == RecordingStates.recording)
                            {
                                if (iCountBeats > (iRecordBeats + iCountInBeats))
                                {
                                    Dispatcher.Invoke(() => PressPlayRecord());
                                }
                            }


                        }
                    }
                    if (!bMetroMute && bCountingIn && bMetroClick) inputPatcher.Click(true);
                }
            }

            e.WrittenToOutputBuffers = true;
        }

        void ResetAsio()
        {
            if (asioOut != null)
            {
                asioOut.Stop();
                asioOut = null;
            }
        }


        // ----------------------------------------------------------------------------
        // MIDI Driver Interface 

        private void ChangeMIDIDevice(string deviceName)
        {
            int deviceId;
            for (deviceId = 0; deviceId < MidiIn.NumberOfDevices; deviceId++)
            {
                if (deviceName == MidiIn.DeviceInfo(deviceId).ProductName) break;
            }

            if (deviceId >= MidiIn.NumberOfDevices) deviceId = 0;

            if (midiIn != null) midiIn.Dispose();
            midiIn = new MidiIn(deviceId);
            midiIn.MessageReceived += MidiReceived;
            midiIn.ErrorReceived += MidiError;
            midiIn.Start();

            //Settings.Default.MIDIDriverName = MidiIn.DeviceInfo(deviceId).ProductName.ToString();
            //cbMidiDevice.Text = MidiIn.DeviceInfo(deviceId).ProductName.ToString();
        }

        private void midiTimer_Tick(object sender, EventArgs e)
        {
            tbMidi.Background = bMidiDefault;
        }

        private void MidiReceived(object sender, MidiInMessageEventArgs e)
        {
            //Debug.WriteLine("0x{0:X8} {1} {2} {3}", e.RawMessage, e.MidiEvent, e.MidiEvent.CommandCode, e.MidiEvent.Channel);
            if (iMidiLearn != 0)
            {
                if (iMidiLearn == 1)
                {
                    iMidiPlayRecord = e.RawMessage;
                    bMidiPlayRecord.Dispatcher.Invoke(() => { bMidiPlayRecord.Background = bMidiPlayRecordDefault; });
                } else if (iMidiLearn == 2)
                {
                    iMidiStop = e.RawMessage;
                    bMidiStop.Dispatcher.Invoke(() => { bMidiStop.Background = bMidiStopDefault; });
                }
                iMidiLearn = 0;
            }
            else
            {
                if (e.RawMessage == iMidiPlayRecord)
                {
                    Dispatcher.Invoke(() => { PressPlayRecord(); });
                } else if (e.RawMessage == iMidiStop)
                {
                    Dispatcher.Invoke(() => { StopRecorder(); });
                }
                tbMidi.Dispatcher.Invoke(() => { tbMidi.Background = Brushes.Orange; });
                if (!midiTimer.IsEnabled) midiTimer.Start();
            }
        }

        private void MidiError(object sender, MidiInMessageEventArgs e)
        {
            //Debug.WriteLine("! 0x{0:X8} {1}", e.RawMessage, e.MidiEvent);
            tbMidi.Dispatcher.Invoke(() => { tbMidi.Background = Brushes.Red; });
            if (!midiTimer.IsEnabled) midiTimer.Start();
        }

        private void bMidiPlayRecord_Click(object sender, RoutedEventArgs e)
        {
            if (iMidiLearn == 1)
            {
                bMidiPlayRecord.Background = bMidiPlayRecordDefault;
                iMidiLearn = 0;
                return;
            }
            if (iMidiLearn == 2)
            {
                bMidiStop.Background = bMidiStopDefault;
            }
            bMidiPlayRecord.Background = Brushes.Orange;
            iMidiLearn = 1;
        }

        private void bMidiStop_Click(object sender, RoutedEventArgs e)
        {
            if (iMidiLearn == 2)
            {
                bMidiStop.Background = bMidiStopDefault;
                iMidiLearn = 0;
                return;
            }
            if (iMidiLearn == 1)
            {
                bMidiPlayRecord.Background = bMidiPlayRecordDefault;
            }
            bMidiStop.Background = Brushes.Orange;
            iMidiLearn = 2;
        }


        // ----------------------------------------------------------------------------
        // Metronome 

        private void StartMetronome(bool useCountIn = false)
        {
            if (useCountIn)
            {
                iCountInBeats = int.Parse(ibCountInBeats.Text);
            } else
            {
                iCountInBeats = -1;
            }
            iCountBeats = 0;
            bCountingIn = bMetroPlaying = bMetroClick = halfBeat = true;
        }

        private void StopMetronome(bool stopJustClick = true)
        {
            if (!stopJustClick)
            {
                bMetroPlaying = false;
                eBeat.Fill = Brushes.Transparent;
                bMetroPlay.Foreground = Brushes.Black;
            }
            bMetroClick = false;
        }

        private void ibTempo_LostMouseCapture(object sender, MouseEventArgs e)
        {
            iTempo = int.Parse(ibTempo.Text);
            halfBeatMS = (int)(1000f * 60f / (float)iTempo / 2f);
    }

        private void bMetroPlay_Click(object sender, RoutedEventArgs e)
        {
            if (bMetroPlaying)
            {
                StopMetronome(false);
            }
            else
            {
                StartMetronome();
                bMetroPlay.Foreground = Brushes.Green;
            }
        }


        // ----------------------------------------------------------------------------
        // IntBox Interface
        //
        // Use this set of events to have the IntBoxController manage all IntBox groups of controls
        // Use the _LostMouseCapture event on the TextBlock control to act on changes of the control value

        private void IntBox_PreviewMouseDown(object sender, MouseButtonEventArgs e) => ibController.PreviewMouseDown(sender, e);
        private void IntBox_PreviewMouseUp(object sender, MouseButtonEventArgs e) => ibController.PreviewMouseUp(sender, e);
        private void IntBox_MouseMove(object sender, MouseEventArgs e) => ibController.MouseMove(sender, e, cMain);
        private void IntBox_MouseWheel(object sender, MouseWheelEventArgs e) => ibController.MouseWheel(sender, e);
        private void IntBox_PreviewKeyDown(object sender, KeyEventArgs e) => ibController.PreviewKeyDown(sender, e);

        private void IntDial_PreviewMouseDown(object sender, MouseButtonEventArgs e) => idController.PreviewMouseDown(sender, e);
        private void IntDial_PreviewMouseUp(object sender, MouseButtonEventArgs e) => idController.PreviewMouseUp(sender, e);
        private void IntDial_MouseMove(object sender, MouseEventArgs e)
        {
            Ellipse el = (Ellipse)sender;
            if (idController.MouseMove(sender, e))
            {
                if (el == eMetroVolume)
                {
                    inputPatcher.clickVolume = (float)(clickVolumeScale * Math.Pow(idController.GetValue(idMetroVolume), clickVolumeLogExp));
                }
                else if (el == eMetroPan)
                {
                    inputPatcher.clickPan = ((float)idController.GetValue(idMetroPan) / 50f) - 1f;
                }
                else if (el == eNoiseGate)
                {
                    inputPatcher.SetNoiseGateThreshold(idController.GetValue(idNoiseGate));
                }
            }
        }

        private void idMetroVolume_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            idMetroVolume.Visibility = (idMetroVolume.Visibility == Visibility.Hidden) ? Visibility.Visible : Visibility.Hidden;
            bMetroMute = (idMetroVolume.Visibility == Visibility.Hidden);
        }

        private void idMetroPan_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            idMetroPan.Data = idController.GetArc(50, 1);
            idController.ChangeValue(idMetroPan, 50);
            inputPatcher.clickPan = 0;
            idController.doubleClicked = true;
        }

        private void idNoiseGateAmount_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            idNoiseGate.Visibility = (idNoiseGate.Visibility == Visibility.Hidden) ? Visibility.Visible : Visibility.Hidden;
            inputPatcher.EnableNoiseGate(idNoiseGate.Visibility == Visibility.Visible);
        }

        private void bSettings_Click(object sender, RoutedEventArgs e)
        {
            StopMetronome();
            StopRecorder();

            SettingsWindow w = new();
            if ((bool)w.ShowDialog())
            {
                ChangeAudioDriver(Settings.Default.AudioDriverName);
                ChangeMIDIDevice(Settings.Default.MIDIDriverName);
            }
        }
    }
}
