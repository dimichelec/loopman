using System;
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
        private int iTimeSigNum;
        private int iTimeSigDen;
        private int iCountinBars;
        private int iCountBars;
        private int iCountBeats;
        private bool bCountingIn;
        private bool bMetroPlaying;
        private bool bMetroMute;
        private int beatMS = (int)(1000f * 60f / 110f);
        private int barsToRecord;

        private Stopwatch timecheck = new Stopwatch();


        private const double clickVolumeLogExp = 2.718;
        private float clickVolumeScale = (float)(1 / Math.Pow(100, clickVolumeLogExp));

        private IntBoxController ibController = new IntBoxController();
        private IntDialController idController = new IntDialController();

        private VUMeterController vuMeter = null;


        // ----------------------------------------------------------------------------
        // Main Window

        public MainWindow()
        {
            InitializeComponent();

            // recall window location
            this.Left = Settings.Default.WindowLeft;
            this.Top = Settings.Default.WindowTop;

            LoadDeviceLists();

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

            // init recorder parameters
            ibCountInBeats.Text = Settings.Default.CountInBeats.ToString();
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

        private void LoadDeviceLists()
        {
            // audio devices
            cbDevice.SelectedItem = -1;
            cbDevice.Items.Clear();

            foreach (string name in AsioOut.GetDriverNames())
            {
                ComboBoxItem o = new ComboBoxItem();
                string s = name;
                if (!s.Contains("asio", StringComparison.OrdinalIgnoreCase)) s += " ASIO";
                o.Content = s;
                if (!TestAudioDriver(name)) { o.IsEnabled = false; }
                _ = cbDevice.Items.Add(o);
            }

            if (cbDevice.Items.Count > 0)
                cbDevice.Text = cbDevice.Items[0].ToString();

            // midi devices
            cbMidiDevice.SelectedItem = -1;
            cbMidiDevice.Items.Clear();

            for (int i = 0; i < MidiIn.NumberOfDevices; i++)
            {
                ComboBoxItem o = new ComboBoxItem();
                o.Content = MidiIn.DeviceInfo(i).ProductName;
                //if (!TestAudioDriver(name)) o.IsEnabled = false;
                cbMidiDevice.Items.Add(o);
            }

            if (cbMidiDevice.Items.Count > 0)
                cbMidiDevice.Text = cbMidiDevice.Items[0].ToString();
        }

        private void tbFocusOnNext(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TraversalRequest request = new TraversalRequest(FocusNavigationDirection.Next);
                MoveFocus(request);
            }
        }

        private void tbSelectAll(object sender, RoutedEventArgs e)
        {
            TextBox tb = (TextBox)sender;
            tb.Dispatcher.BeginInvoke(new Action(() => tb.SelectAll()));
        }


        // ----------------------------------------------------------------------------

        public void GetDevices()
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
            if (bMetroPlaying)
            {
                bMetroPlaying = false;
                bMetroPlay.Foreground = Brushes.Black;
            }
            else
            {
                iCountBars = iCountBeats = 1;
            }
            ResetRecorderState();
            //if (bMetroPlaying)
            //{
            //    metronome.Stop();
            //    bMetroPlay.Foreground = Brushes.Black;
            //}
            //else metronome.Reset();
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
                    //metronome.Play(int.Parse(ibCountInBars.Text));
                    iCountinBars = int.Parse(ibCountInBeats.Text);
                    iCountBars = 0;
                    iCountBeats = 4;
                    bCountingIn = bMetroPlaying = true;
                    break;

                case RecordingStates.recording:
                    inputPatcher.MarkLoopEnd();
                    inputPatcher.Play();
                    inputPatcher.isRecording = false;
                    rPlayRecord.Stroke = Brushes.Green;
                    recordingState = RecordingStates.playback;
                    break;

                default:
                    StopRecorder();
                    break;
            }
        }

        private void bPlayRecord_Click(object sender, RoutedEventArgs e)
        {
            //barsToRecord = int.Parse(ibBars.Text);
            PressPlayRecord();
        }

        private int RecordedBars;
        private void NewBarCounted()
        {
            if (recordingState == RecordingStates.armed)
            {
                //if (int.Parse(ibCountBars.Text) > int.Parse(ibCountInBeats.Text))
                //{
                //    inputPatcher.MarkLoopStart();
                //    RecordedBars = 0;
                //    rPlayRecord.Stroke = Brushes.Red;
                //    recordingState = RecordingStates.recording;
                //}
            } else if (recordingState == RecordingStates.recording)
            {
                //if (++RecordedBars >= int.Parse(ibBars.Text))
                //{
                //    inputPatcher.MarkLoopEnd();
                //    inputPatcher.Play();
                //    rPlayRecord.Stroke = Brushes.Green;
                //    recordingState = RecordingStates.playback;
                //    inputPatcher.isRecording = false;
                //}

            }
        }

        private void bStop_Click(object sender, RoutedEventArgs e) => StopRecorder();


        // ----------------------------------------------------------------------------
        // Audio Driver Interface 

        private void cbDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cbDevice.SelectedItem == null) return;
            ChangeAudioDriver(((ComboBoxItem)cbDevice.SelectedItem).Content.ToString());
        }

        private bool TestAudioDriver(string driverName)
        {
            AsioDriver driver;
            try
            {
                driver = AsioDriver.GetAsioDriverByName(driverName);
            }
            catch (Exception)
            {
                return false;
            }
            if (!driver.Init(IntPtr.Zero)) return false;
            driver.ReleaseComAsioDriver();
            return true;
        }

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

            Settings.Default.AudioDriverName = driverName;
            cbDevice.Text = driverName;

            timecheck.Start();
        }

        void OnAsioOutAudioAvailable(object sender, AsioAudioAvailableEventArgs e)
        {
            inputPatcher.ProcessBuffer(
                e.InputBuffers, e.OutputBuffers,
                e.SamplesPerBuffer, e.AsioSampleType
            );

            if (bMetroPlaying && (timecheck.ElapsedMilliseconds >= beatMS))
            {
                // we're running, ready, and a new 16th has elapsed
                timecheck.Restart();
                bool flag = false;
                if (iCountBeats < iTimeSigNum) iCountBeats++;
                else
                {
                    iCountBeats = 1;
                    iCountBars++;
                    if ((iCountinBars != -1) && (iCountBars > iCountinBars)) bCountingIn = false;
                    if (iCountBars > 99) iCountBars = 1;
                    //Dispatcher.Invoke(() => { ibCountBars.Text = iCountBars.ToString(); });
                    flag = true;

                    if (recordingState == RecordingStates.armed)
                    {
                        if (iCountBars > iCountinBars)
                        {
                            inputPatcher.MarkLoopStart();
                            RecordedBars = 0;
                            Dispatcher.Invoke(() => { rPlayRecord.Stroke = Brushes.Red; });
                            recordingState = RecordingStates.recording;
                        }
                    }
                    else if (recordingState == RecordingStates.recording)
                    {
                        if (++RecordedBars >= barsToRecord)
                        {
                            inputPatcher.MarkLoopEnd();
                            inputPatcher.Play();
                            Dispatcher.Invoke(() => { rPlayRecord.Stroke = Brushes.Green; });
                            recordingState = RecordingStates.playback;
                            inputPatcher.isRecording = false;
                        }
                    }
                }
                //Dispatcher.Invoke(() => {  });
                if (!bMetroMute && bCountingIn) inputPatcher.Click(flag);
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

        private void cbMidiDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cbMidiDevice.SelectedItem == null) return;
            ChangeMIDIDevice(((ComboBoxItem)cbMidiDevice.SelectedItem).Content.ToString());
        }

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

            Settings.Default.MIDIDriverName = MidiIn.DeviceInfo(deviceId).ProductName.ToString();
            cbMidiDevice.Text = MidiIn.DeviceInfo(deviceId).ProductName.ToString();
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
        // Metronome Interface 

        private void ibTempo_LostMouseCapture(object sender, MouseEventArgs e) => iTempo = int.Parse(ibTempo.Text);

        private void bMetroPlay_Click(object sender, RoutedEventArgs e)
        {
            if (bMetroPlaying)
            {
                bMetroPlaying = false;
                bMetroPlay.Foreground = Brushes.Black;
            }
            else
            {
                iCountinBars = int.Parse(ibCountInBeats.Text);
                iCountBars = 0;
                iCountBeats = 4;
                bMetroPlaying = true;
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
        private void IntBox_MouseMove(object sender, MouseEventArgs e) => ibController.MouseMove(sender, e, gMain);
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
    }
}
