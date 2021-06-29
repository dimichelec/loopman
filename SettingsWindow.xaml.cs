using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using NAudio.Wave;
using NAudio.Wave.Asio;
using NAudio.Midi;


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

            cbDevice.Text = Settings.Default.AudioDriverName;
            cbMidiDevice.Text = Settings.Default.MIDIDriverName;

            bMidiPlayRecordDefault = bMidiPlayRecord.Background;
            bMidiStopDefault = bMidiStop.Background;

            tbMidiPlayRecordMap.Text = Settings.Default.MIDIPlayRecord.ToString("X8");
            tbMidiStopMap.Text = Settings.Default.MIDIStop.ToString("X8");
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (midiIn != null) midiIn.Dispose();

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


        private void cbDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //cbDevice.Text = driverName;
        }

        private void cbMidiDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ChangeMIDIDevice(((ComboBoxItem)cbMidiDevice.SelectedItem).Content.ToString());
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


        private void bOK_Click(object sender, RoutedEventArgs e)
        {
            Settings.Default.AudioDriverName = ((ComboBoxItem)cbDevice.SelectedItem).Content.ToString();
            Settings.Default.MIDIDriverName = ((ComboBoxItem)cbMidiDevice.SelectedItem).Content.ToString();

            Settings.Default.MIDIPlayRecord = iMidiPlayRecord;
            Settings.Default.MIDIStop = iMidiStop;

            DialogResult = true;
        }

        private void bCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
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
