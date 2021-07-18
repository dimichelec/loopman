using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Diagnostics;
using System.Management;
using System.Windows.Threading;
using System.Windows.Shapes;

using NAudio.Wave;
using NAudio.Wave.Asio;
using NAudio.Wave.SampleProviders;
using NAudio.Midi;
using NAudio.CoreAudioApi;
using NAudio.Utils;


namespace loopman
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 
    public partial class MainWindow : Window
    {
        private AsioOut asioOut;
        private InputPatcher inputPatcher;
        private int inputLatencies;
        private int outputLatencies;

        private WasapiCapture wasapiIn;
        private WasapiOut wasapiOut;

        private Brush bMonoInputDefault;

        private MidiIn midiIn;
        private DispatcherTimer midiTimer;
        private Brush bMidiDefault;
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
            ChangeAudioDriver(Settings.Default.AudioInDriverName);
            ChangeMIDIDevice(Settings.Default.MIDIDriverName);

            // init noise gate controls
            idController.ChangeValue(idNoiseGate, Settings.Default.NoiseGateThreshold);
            idNoiseGate.Data = idController.GetArc(100 - Settings.Default.NoiseGateThreshold, 1, 8);
            if (inputPatcher != null)
                inputPatcher.SetNoiseGateThreshold(Settings.Default.NoiseGateThreshold);
            idNoiseGate.Visibility = (Settings.Default.NoiseGateEnabled) ? Visibility.Visible : Visibility.Hidden;
            if (inputPatcher != null)
                inputPatcher.EnableNoiseGate(Settings.Default.NoiseGateEnabled);

            // init midi controller
            iMidiPlayRecord = Settings.Default.MIDIPlayRecord;
            iMidiStop = Settings.Default.MIDIStop;
            bMidiDefault = tbMidi.Background;

            // init metronome controller
            ibTempo.Text = Settings.Default.Tempo.ToString();

            idController.ChangeValue(idMetroVolume, Settings.Default.MetroVolume);
            idMetroVolume.Data = idController.GetArc(100 - Settings.Default.MetroVolume);
            if (inputPatcher != null)
                inputPatcher.clickVolume = (float)(clickVolumeScale * Math.Pow(Settings.Default.MetroVolume, clickVolumeLogExp));
            idMetroVolume.Visibility = (Settings.Default.MetroMute) ? Visibility.Hidden : Visibility.Visible;
            bMetroMute = Settings.Default.MetroMute;

            int i = (int)((Settings.Default.MetroPan + 1f) * 50f);
            idController.ChangeValue(idMetroPan, i);
            idMetroPan.Data = idController.GetArc(100 - i, 1);
            if (inputPatcher != null)
                inputPatcher.clickPan = Settings.Default.MetroPan;
            iTempo = int.Parse(ibTempo.Text);
            halfBeatMS = (int)(1000f * 60f / (float)iTempo / 2f);

            // init recorder parameters
            ibCountInBeats.Text = Settings.Default.CountInBeats.ToString();
            ibRecordBeats.Text = Settings.Default.RecordBeats.ToString();
            rPlayRecord.Stroke = Brushes.Transparent;

            bMonoInputDefault = bMonoInput.Foreground;
            if (inputPatcher != null)
                inputPatcher.MonoRouting = Settings.Default.MonoInput;
            if (Settings.Default.MonoInput) { bMonoInput.Foreground = Brushes.Green; }

            DispatcherTimer timServices = new DispatcherTimer();
            timServices.Tick += timServices_Tick;
            timServices.Interval = new TimeSpan(1000); // 100nS units
            timServices.Start();
        }

        private void Window_ContentRendered(object sender, EventArgs e)
        {
            if ((asioOut == null) && (wasapiIn == null)) Dispatcher.Invoke(() => OpenSettingsDialog());
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Settings.Default.WindowLeft = this.Left;
            Settings.Default.WindowTop = this.Top;

            // save noise gate parameters
            if (inputPatcher != null)
            {
                Settings.Default.NoiseGateThreshold = inputPatcher.GetNoiseGateThreshold();
                Settings.Default.MonoInput = inputPatcher.MonoRouting;
            }
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
            ResetWasapi();
            Application.Current.Shutdown();
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

        private void timServices_Tick(object sender, EventArgs e)
        {
            if (inputPatcher != null)
            {
                if (inputPatcher.iRecordMax > 0)
                    pbMemory.Value = (int)(100f * (float)inputPatcher.iRecord / inputPatcher.iRecordMax);
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
            if (inputPatcher != null)
            {
                inputPatcher.Stop();
            }
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
            ResetWasapi();
            if (vuMeter != null) vuMeter.SetPatcher(null);
            inputLatencies = outputLatencies = 0;

            if (driverName == null) return;
            if (driverName == "") return;

            // first try getting the new device's driver capabilities
            AsioDriver drv;
            try
            {
                drv = AsioDriver.GetAsioDriverByName(driverName);

                if (!drv.Init(IntPtr.Zero)) return;

                drv.GetLatencies(out inputLatencies, out outputLatencies);

                // open output device
                asioOut = new AsioOut(driverName);
                asioOut.InputChannelOffset = 0;
                asioOut.ChannelOffset = 0;

                inputPatcher = new InputPatcher((int)drv.GetSampleRate(), 2, 2);

                asioOut.AudioAvailable += OnAsioOutAudioAvailable;
                asioOut.InitRecordAndPlayback(new SampleToWaveProvider(inputPatcher), 2, 0);
                asioOut.Play();
            }
            catch (Exception ex)
            {
                MessageBox.Show("A handled exception just occurred: " + ex.Message, "Exception Sample", MessageBoxButton.OK, MessageBoxImage.Warning);

                MMDeviceEnumerator deviceEnum = new();
                MMDeviceCollection devs = deviceEnum.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                for (int i = 0; i < devs.Count; i++)
                {
                    if (devs[i].FriendlyName == Settings.Default.AudioInDriverName)
                    {
                        wasapiIn = new(devs[i]); //new WasapiCapture(devs[i]);
                        wasapiIn.ShareMode = AudioClientShareMode.Shared;
                        break;
                    }
                }
                
                devs = deviceEnum.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                for (int i = 0; i < devs.Count; i++)
                {
                    if (devs[i].FriendlyName == Settings.Default.AudioOutDriverName)
                    {
                        wasapiOut = new WasapiOut(devs[i], AudioClientShareMode.Shared, true, 10);
                        break;
                    }
                }
                
                bwp = new BufferedWaveProvider(wasapiIn.WaveFormat);
                bwp.DiscardOnBufferOverflow = true;

                inputPatcher = new InputPatcher((int)wasapiIn.WaveFormat.SampleRate, 2, 2);

                wasapiIn.DataAvailable += OnWasapiInDataAvailable;
                wasapiOut.Init(bwp);
                wasapiIn.StartRecording();
                wasapiOut.Play();
            }

            vuMeter.SetPatcher(inputPatcher);

            inputPatcher.clickVolume = (float)(clickVolumeScale * Math.Pow(Settings.Default.MetroVolume, clickVolumeLogExp));

            inputPatcher.SetNoiseGateThreshold(Settings.Default.NoiseGateThreshold);
            inputPatcher.EnableNoiseGate(Settings.Default.NoiseGateEnabled);

            timecheck.Start();
        }

        void OnAsioOutAudioAvailable(object sender, AsioAudioAvailableEventArgs e)
        {
            inputPatcher.ProcessASIOBuffer(
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
                    if (iCountInBeats > 0)
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
            timecheck.Stop();
            if (asioOut != null)
            {
                asioOut.Stop();
                asioOut = null;
            }
        }

        private float tmp = 0.0f;

        private BufferedWaveProvider bwp;

        private void OnWasapiInDataAvailable(object sender, WaveInEventArgs e)
        {

            inputPatcher.ProcessWasapiBuffer(e.Buffer, e.BytesRecorded, bwp);

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
                    if (iCountInBeats > 0)
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

        }

        void ResetWasapi()
        {
            if (wasapiIn != null)
            {
                wasapiIn.StopRecording();
                wasapiIn.DataAvailable -= OnWasapiInDataAvailable;
                wasapiIn = null;
            }

            if (wasapiOut != null)
            {
                wasapiOut.Stop();
                wasapiOut = null;
            }
        }

        private void bMonoInput_Click(object sender, RoutedEventArgs e)
        {
            if (inputPatcher.MonoRouting)
            {
                inputPatcher.MonoRouting = false;
                bMonoInput.Foreground = bMonoInputDefault;
            }
            else
            {
                inputPatcher.MonoRouting = true;
                bMonoInput.Foreground = Brushes.Green;
            }

        }


        // ----------------------------------------------------------------------------
        // MIDI Driver Interface 

        private void ChangeMIDIDevice(string deviceName)
        {
            if (MidiIn.NumberOfDevices == 0) return;
            if (deviceName == null) return;
            if (deviceName == "") return;

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
        }

        private void midiTimer_Tick(object sender, EventArgs e)
        {
            tbMidi.Background = bMidiDefault;
        }

        private void MidiReceived(object sender, MidiInMessageEventArgs e)
        {
            if (e.RawMessage == iMidiPlayRecord)
            {
                Dispatcher.Invoke(() => { PressPlayRecord(); });
            }
            else if (e.RawMessage == iMidiStop)
            {
                Dispatcher.Invoke(() => { StopRecorder(); });
            }
            tbMidi.Dispatcher.Invoke(() => { tbMidi.Background = Brushes.Orange; });
            if (!midiTimer.IsEnabled) midiTimer.Start();
        }

        private void MidiError(object sender, MidiInMessageEventArgs e)
        {
            //Debug.WriteLine("! 0x{0:X8} {1}", e.RawMessage, e.MidiEvent);
            tbMidi.Dispatcher.Invoke(() => { tbMidi.Background = Brushes.Red; });
            if (!midiTimer.IsEnabled) midiTimer.Start();
        }


        // ----------------------------------------------------------------------------
        // Metronome 

        private void StartMetronome(bool useCountIn = false)
        {
            if (useCountIn)
            {
                iCountInBeats = int.Parse(ibCountInBeats.Text);
            }
            else
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
            OpenSettingsDialog();
        }

        private void OpenSettingsDialog()
        {
            StopMetronome();
            StopRecorder();

            if (midiIn != null) midiIn.Dispose();

            SettingsWindow w = new();
            if ((bool)w.ShowDialog())
            {
                ChangeAudioDriver(Settings.Default.AudioInDriverName);

                iMidiPlayRecord = Settings.Default.MIDIPlayRecord;
                iMidiStop = Settings.Default.MIDIStop;
            }
            ChangeMIDIDevice(Settings.Default.MIDIDriverName);
        }

    }
}
