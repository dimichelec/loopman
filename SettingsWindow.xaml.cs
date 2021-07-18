using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using NAudio.Wave;
using NAudio.Wave.Asio;
using NAudio.Midi;
using NAudio.CoreAudioApi;


namespace loopman
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private MidiIn midiIn;
        private Brush bMidiPlayRecordDefault;
        private Brush bMidiStopDefault;
        private int iMidiLearn;
        private int iMidiPlayRecord;
        private int iMidiStop;


        public SettingsWindow()
        {
            InitializeComponent();

            LoadDeviceLists();

            if ((Settings.Default.AudioInDriverName != "") && (FindInContent(cbDeviceIn, Settings.Default.AudioInDriverName) >= 0))
            {
                cbDeviceIn.Text = Settings.Default.AudioInDriverName;
            }

            if ((Settings.Default.AudioOutDriverName != "") && (FindInContent(cbDeviceOut, Settings.Default.AudioOutDriverName) >= 0))
            {
                cbDeviceOut.Text = Settings.Default.AudioOutDriverName;
            }

            if ((Settings.Default.MIDIDriverName != "") && (FindInContent(cbMidiDevice, Settings.Default.MIDIDriverName) >= 0))
            {
                cbMidiDevice.Text = Settings.Default.MIDIDriverName;
            }

            ChangeMIDIDevice(Settings.Default.MIDIDriverName);

            bMidiPlayRecordDefault = bMidiPlayRecord.Background;
            bMidiStopDefault = bMidiStop.Background;

            tbMidiPlayRecordMap.Text = Settings.Default.MIDIPlayRecord.ToString("X8");
            tbMidiStopMap.Text = Settings.Default.MIDIStop.ToString("X8");
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (midiIn != null) { midiIn.Dispose(); }

        }

        private void LoadDeviceLists()
        {
            // audio devices
            cbDeviceIn.SelectedItem = -1;
            cbDeviceIn.Items.Clear();

            cbDeviceOut.SelectedItem = -1;
            cbDeviceOut.Items.Clear();

            foreach (string name in AsioOut.GetDriverNames())
            {
                ComboBoxItem o = new();
                string s = name;
                if (!s.Contains("asio", StringComparison.OrdinalIgnoreCase)) s += " ASIO";
                o.Content = s;
                if (!TestAudioDriver(name)) { o.IsEnabled = false; }
                _ = cbDeviceIn.Items.Add(o);
                //if (o.IsEnabled) {
                //    ComboBoxItem p = new();
                //    p.Content = s;
                //    _ = cbDeviceOut.Items.Add(p);
                //}
            }

            MMDeviceEnumerator deviceEnum = new();
            MMDeviceCollection devicesIn = deviceEnum.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active); //.ToList();

            for (int i = 0; i < devicesIn.Count; i++)
            {
                ComboBoxItem o = new ComboBoxItem();
                o.Content = devicesIn[i].FriendlyName;
                //if (!TestAudioDriver(name)) o.IsEnabled = false;
                _ = cbDeviceIn.Items.Add(o);
            }

            foreach (ComboBoxItem dev in cbDeviceIn.Items)
            {
                if (dev.IsEnabled)
                {
                    cbDeviceIn.Text = dev.Content.ToString();
                    break;
                }
            }

            MMDeviceCollection devicesOut = deviceEnum.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active); //.ToList();
            for (int i = 0; i < devicesOut.Count; i++)
            {
                ComboBoxItem o = new ComboBoxItem();
                o.Content = devicesOut[i].FriendlyName;
                //if (!TestAudioDriver(name)) o.IsEnabled = false;
                _ = cbDeviceOut.Items.Add(o);
            }

            foreach (ComboBoxItem dev in cbDeviceOut.Items)
            {
                if (dev.IsEnabled)
                {
                    if (cbDeviceIn.Text.Contains("asio", StringComparison.OrdinalIgnoreCase) && (dev.Content.ToString() == cbDeviceIn.Text))
                    {
                        cbDeviceOut.Text = dev.Content.ToString();
                        break;
                    }
                }
            }

            if (cbDeviceOut.Text == "")
            {
                foreach (ComboBoxItem dev in cbDeviceOut.Items)
                {
                    if (dev.IsEnabled)
                    {
                        cbDeviceOut.Text = dev.Content.ToString();
                        break;
                    }
                }
            }

            // midi devices
            cbMidiDevice.SelectedItem = -1;
            cbMidiDevice.Items.Clear();

            for (int i = 0; i < MidiIn.NumberOfDevices; i++)
            {
                ComboBoxItem o = new ComboBoxItem();
                o.Content = MidiIn.DeviceInfo(i).ProductName;
                if (!TestMidiDriver(i)) o.IsEnabled = false;
                cbMidiDevice.Items.Add(o);
            }

            if (cbMidiDevice.Items.Count == 0)
            {
                cbMidiDevice.IsEnabled = false;
            }
            else
            {
                foreach (ComboBoxItem dev in cbMidiDevice.Items)
                {
                    if (dev.IsEnabled)
                    {
                        cbMidiDevice.Text = dev.Content.ToString();
                        break;
                    }
                }
            }
                
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
            if (!driver.Init(IntPtr.Zero)) { return false; }
            driver.ReleaseComAsioDriver();
            return true;
        }

        private bool TestMidiDriver(int driverNumber)
        {
            MidiIn driver;
            try
            {
                driver = new(driverNumber);
                driver.Dispose();
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }


        private int FindInContent(ComboBox cb, string s)
        {
            int i = 0;
            foreach (ComboBoxItem dev in cb.Items)
            {
                if (dev.Content.ToString() == s) { return i; }
                i++;
            }
            return -1;
        }


        // ----------------------------------------------------------------------------
        // MIDI Driver Interface 

        private void ChangeMIDIDevice(string deviceName)
        {
            bMidiPlayRecord.IsEnabled = false;
            bMidiStop.IsEnabled = false;

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

            bMidiPlayRecord.IsEnabled = true;
            bMidiStop.IsEnabled = true;
        }

        private void MidiReceived(object sender, MidiInMessageEventArgs e)
        {
            //Debug.WriteLine("0x{0:X8} {1} {2} {3}", e.RawMessage, e.MidiEvent, e.MidiEvent.CommandCode, e.MidiEvent.Channel);
            if (iMidiLearn != 0)
            {
                if (iMidiLearn == 1)
                {
                    iMidiPlayRecord = e.RawMessage;
                    Dispatcher.Invoke(() => { 
                        bMidiPlayRecord.Background = bMidiPlayRecordDefault;
                        tbMidiPlayRecordMap.Text = e.RawMessage.ToString("X8");
                    });
                }
                else if (iMidiLearn == 2)
                {
                    iMidiStop = e.RawMessage;
                    Dispatcher.Invoke(() => { 
                        bMidiStop.Background = bMidiStopDefault;
                        tbMidiStopMap.Text = e.RawMessage.ToString("X8");
                    });
                }
                iMidiLearn = 0;
            }
        }

        private void MidiError(object sender, MidiInMessageEventArgs e)
        {
            //Debug.WriteLine("! 0x{0:X8} {1}", e.RawMessage, e.MidiEvent);
        }


        // ----------------------------------------------------------------------------
        // Control Event Handlers

        private void bOK_Click(object sender, RoutedEventArgs e)
        {
            Settings.Default.AudioInDriverName = (cbDeviceIn.SelectedItem != null) ? ((ComboBoxItem)cbDeviceIn.SelectedItem).Content.ToString() : "";
            Settings.Default.AudioOutDriverName = (cbDeviceOut.SelectedItem != null) ? ((ComboBoxItem)cbDeviceOut.SelectedItem).Content.ToString() : "";

            Settings.Default.MIDIDriverName = (cbMidiDevice.SelectedItem != null) ? ((ComboBoxItem)cbMidiDevice.SelectedItem).Content.ToString() : "";

            Settings.Default.MIDIPlayRecord = iMidiPlayRecord;
            Settings.Default.MIDIStop = iMidiStop;

            DialogResult = true;
        }

        private void bCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void cbDeviceIn_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            cbDeviceOut.Visibility =
                (cbDeviceIn.SelectedItem.ToString().Contains("asio", StringComparison.OrdinalIgnoreCase)) ?
                Visibility.Hidden : Visibility.Visible;
        }

        private void cbMidiDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ChangeMIDIDevice(((ComboBoxItem)cbMidiDevice.SelectedItem).Content.ToString());
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

    }
}
