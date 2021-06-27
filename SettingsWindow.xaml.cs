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
        public SettingsWindow()
        {
            InitializeComponent();

            LoadDeviceLists();

            cbDevice.Text = Settings.Default.AudioDriverName;
            cbMidiDevice.Text = Settings.Default.MIDIDriverName;

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
        }

        private void bOK_Click(object sender, RoutedEventArgs e)
        {
            Settings.Default.AudioDriverName = ((ComboBoxItem)cbDevice.SelectedItem).Content.ToString();
            Settings.Default.MIDIDriverName = ((ComboBoxItem)cbMidiDevice.SelectedItem).Content.ToString();

            DialogResult = true;
        }

        private void bCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
