using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using DirectiveServer.libs;
using DirectiveServer.libs.Enums;
using DirectiveServer.libs.Helper;

namespace WpfApp
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        public SerialPort comPort = new SerialPort();
        public Socket server = null;
        private string address;
        private int port = 8080;
        private bool isStart = false;
        private byte[] data = new byte[256];
        private double MULTI_PERCENT = 0.1;
        private double BAD_PERCENT = 0.0;

        private ConcurrentDictionary<int, bool> isRunning = new ConcurrentDictionary<int, bool>();
        private ConcurrentDictionary<int, bool> isPausing = new ConcurrentDictionary<int, bool>();
        private Dictionary<int, Timer> runTimers = new Dictionary<int, Timer>();
        private Dictionary<int, int> runSpeed = new Dictionary<int, int>();
        private ConcurrentQueue<Tuple<Socket, byte[]>> directiveQueue = new ConcurrentQueue<Tuple<Socket, byte[]>>();

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                address = Environment.GetEnvironmentVariable("IOT_DIRECTIVE_IP") ?? "192.168.1.100";
            }
            catch (Exception)
            {
                //
            }

            isRunning.TryAdd(1, false);
            isRunning.TryAdd(2, false);
            isRunning.TryAdd(3, false);
            isRunning.TryAdd(4, false);
            isRunning.TryAdd(0x80, false);
            isRunning.TryAdd(0x90, false);
            isRunning.TryAdd(0x91, false);

            isPausing.TryAdd(1, false);
            isPausing.TryAdd(2, false);
            isPausing.TryAdd(3, false);
            isPausing.TryAdd(4, false);
            isPausing.TryAdd(0x80, false);
            isPausing.TryAdd(0x90, false);
            isPausing.TryAdd(0x91, false);

            runSpeed.Add(1, 0);
            runSpeed.Add(2, 0);
            runSpeed.Add(3, 0);
            runSpeed.Add(4, 0);
            runSpeed.Add(0x90, 0);

            InitializeComponent();
            this.Closed += MainWindow_Closed;
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            comPort.Close();
            isStart = false;
        }

        private byte[] HexToByte(string msg)
        {
            msg = msg.Replace(" ", "");
            var comBuffer = new byte[msg.Length / 2];

            for (var i = 0; i < msg.Length; i += 2)
                comBuffer[i / 2] = (byte)Convert.ToByte(msg.Substring(i, 2), 16);
            return comBuffer;
        }

        private string ByteToHex(byte[] comByte)
        {
            var builder = new StringBuilder(comByte.Length * 3);
            foreach (var data in comByte)
                builder.Append(Convert.ToString(data, 16).PadLeft(2, '0').PadRight(3, ' '));
            return builder.ToString().ToUpper();
        }

        private void BtnSend_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var newMsg = HexToByte(txtSend.Text);
                comPort.Write(newMsg, 0, newMsg.Length);

                RenderMsg(spSend, txtSend.Text);
            }
            catch (Exception)
            {
                //
            }
           
        }

        private void BtnOpen_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                ClosePort();

                comPort.BaudRate = 9600; 
                comPort.DataBits = 8;   
                comPort.StopBits = StopBits.One;   
                comPort.Parity = Parity.None;
                comPort.PortName = txtCom.Text;
                comPort.Open();
                comPort.DataReceived += ComPort_DataReceived;
                txtStatus.Text = "open";
            }
            catch (Exception ex)
            {
                txtStatus.Text = ex.Message;
                //
            }
        }

        private void ComPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            var bytes = comPort.BytesToRead;
            var comBuffer = new byte[bytes];
            comPort.Read(comBuffer, 0, bytes);
            resolveSendData(new List<byte>(comBuffer));
            RenderMsg(spRece, ByteToHex(comBuffer));
        }

        public void ClosePort()
        {
            if (comPort.IsOpen == true)
            {
                comPort.Close();
                comPort.DataReceived -= ComPort_DataReceived;
            }

            txtStatus.Text = "close";
        }

        public void RenderMsg(ListView lv, string msg)
        {
            if(string.IsNullOrEmpty(msg)) return;
            Dispatcher.Invoke(() =>
            {
                var tb = new TextBlock { Text = msg };
                if (lv.Items.Count > 20) lv.Items.Clear();
                lv.Items.Add(tb);
                lv.ScrollIntoView(lv.Items[lv.Items.Count - 1]);
            });
        }

        private bool ValidateDirective(byte[] bytes)
        {
            if (bytes.Length <= 4) return false;

            var content = bytes.Take(bytes.Length - 2).ToArray();
            var checkCode = bytes.Skip(bytes.Length - 2).Take(2).ToArray();
            var p = DirectiveHelper.GenerateCheckCode(content);

            return p[0] == checkCode[0] && p[1] == checkCode[1];
        }

        private void resolveSendData( List<byte> directiveData)
        {
            if (directiveData.Count <= 2) return;

            var len = ((DirectiveTypeEnum)directiveData[1]).GetDirectiveLength();
            if (len == 0 || directiveData.Count < len)
            {
                return;
            }

            var directive = directiveData.GetRange(0, len).ToArray();

            if (ValidateDirective(directive))
            {
                Send(directive.ToArray());
            }
        }

        private void Send(byte[] bytes)
        {
            try
            {
                if (!ValidateDirective(bytes)) return;
                var ret = processResolve(bytes);

                if (new Random().NextDouble() < MULTI_PERCENT)
                {
                    var pre = ret.Take(2).ToArray();
                    var post = ret.Skip(2).Take(ret.Length - 2).ToArray();
                    comPort.Write(pre, 0, pre.Length);
                    comPort.Write(post, 0, post.Length);
                }
                else
                {
                    comPort.Write(ret, 0, ret.Length);
                }

                RenderMsg(spSend, Common.BytesToString(ret));
            }
            catch (Exception)
            {
                //
            }
        }


        #region xx
        private byte[] processResolve(byte[] bytes)
        {
            byte[] xdata = null;
            switch (bytes[1])
            {
                case 0x00:
                    {
                        var rate = DirectiveHelper.Parse2BytesToNumber(bytes.Skip(2).Take(2).ToArray());
                        var volume = DirectiveHelper.Parse2BytesToNumber(bytes.Skip(4).Take(2).ToArray());
                        xdata = resolveTryStartDirective(bytes);

                        var interval = (int)rate == 0 ? 1000 : (int)((volume / rate) * 60 * 1000);
                        runSpeed[bytes[0]] = (int)rate;

                        Timer timer;
                        if (runTimers.ContainsKey(bytes[0]))
                        {
                            timer = runTimers[bytes[0]];
                            timer.Dispose();
                            runTimers.Remove(bytes[0]);
                        }


                        isRunning[bytes[0]] = true;

                        timer = new Timer(new TimerCallback((p) =>
                        {
                            isRunning[bytes[0]] = false;
                        }), null, interval, 0);

                        runTimers.Add(bytes[0], timer);
                    }
                    break;

                case 0x01:
                    {
                        xdata = bytes;
                        isPausing[bytes[0]] = true;
                        Task.Run(async () =>
                        {
                            await Task.Delay(1000);
                            isPausing[bytes[0]] = false;
                            isRunning[bytes[0]] = false;
                            if (runTimers.ContainsKey(bytes[0]))
                            {
                                var timer = runTimers[bytes[0]];
                                timer?.Dispose();
                                runTimers.Remove(bytes[0]);
                            }
                        });
                    }
                    break;

                case 0x02:
                    xdata = bytes;
                    break;

                case 0x03:
                    {
                        xdata = resolveIdleDirective(bytes);
                    }
                    break;

                case 0x04:
                    {
                        xdata = resolveRunningDirective(bytes);
                    }
                    break;

                case 0x05:
                    {
                        xdata = resolvePausingDirective(bytes);
                    }
                    break;

                default:
                    break;
            }

            if (new Random().NextDouble() < BAD_PERCENT && xdata != null)
            {
                xdata[xdata.Length - 1] = 0xff;
            }

            return xdata;
        }

        private byte[] GetDirectiveId(byte[] bytes)
        {
            var len = bytes.Length;
            var ret = new byte[2];
            ret[0] = bytes[len - 5];
            ret[1] = bytes[len - 4];

            return ret;
        }

        private byte GetDeviceType(byte[] bytes)
        {
            return bytes[bytes.Length - 3];
        }

        private byte[] resolveIdleDirective(byte[] bytes)
        {
            var ids = GetDirectiveId(bytes);
            var content = new byte[] { bytes[0], 0x03, 0x00, 0x00, ids[0], ids[1], GetDeviceType(bytes) };
            var checkCode = DirectiveHelper.GenerateCheckCode(content);

            return content.Concat(checkCode).ToArray();
        }

        private byte[] resolveRunningDirective(byte[] bytes)
        {
            var ids = GetDirectiveId(bytes);
            var rate = new byte[] { 0x00, 0x00 };
            if (isRunning[bytes[0]])
            {
                rate = bytes[0] == 0x91 ? new byte[] { 0x00, 0x20 }  : DirectiveHelper.ParseNumberTo2Bytes(runSpeed[bytes[0]]);// bytes.Skip(4).Take(2).ToArray();
            }

            byte direction = 0x00;
            if(bytes[0] == 3 || bytes[0] == 4)
            {
                direction = 0x01;
            }
            var content = new byte[] { bytes[0], 0x04, 0x00, 0x16, rate[0], rate[1], 0x00, direction, ids[0], ids[1], GetDeviceType(bytes) };
            var checkCode = DirectiveHelper.GenerateCheckCode(content);
            return content.Concat(checkCode).ToArray();
        }

        private byte[] resolvePausingDirective(byte[] bytes)
        {
            var ids = GetDirectiveId(bytes);
            var rate = new byte[] { 0x00, 0x00 };
            if (isPausing[bytes[0]])
            {
                rate = new byte[] { 0x00, 0x23, };
            }
            var content = new byte[] { bytes[0], 0x05, 0x00, 0x12, rate[0], rate[1], 0x01, ids[0], ids[1], GetDeviceType(bytes) };
            var checkCode = DirectiveHelper.GenerateCheckCode(content);
            return content.Concat(checkCode).ToArray();
        }

        private byte[] resolveTryStartDirective(byte[] bytes)
        {
            var ids = GetDirectiveId(bytes);
            var content = new byte[] { bytes[0], 0x00, ids[0], ids[1], GetDeviceType(bytes) };
            var checkCode = DirectiveHelper.GenerateCheckCode(content);
            return content.Concat(checkCode).ToArray();
        }


        #endregion

        private void BtnClose_OnClick(object sender, RoutedEventArgs e)
        {
            ClosePort();
        }
    }
}
